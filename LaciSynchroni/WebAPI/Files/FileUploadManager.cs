using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.Files;
using LaciSynchroni.Common.Routes;
using LaciSynchroni.FileCache;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.UI;
using LaciSynchroni.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LaciSynchroni.WebAPI.Files;

using ServerIndex = int;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileDbManager;
    private readonly SyncConfigService _syncConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ConcurrentDictionary<ServerIndex, ConcurrentDictionary<string, DateTime>> _verifiedUploadedHashesByServer = new();
    /// <summary>
    /// One Cancellation token per server, since we can concurrently upload to each server connected.
    /// </summary>
    private readonly ConcurrentDictionary<ServerIndex, CancellationTokenSource> _cancellationTokens = new();
    /// <summary>
    /// Per-server upload tracking to prevent race conditions during concurrent uploads.
    /// </summary>
    private readonly ConcurrentDictionary<ServerIndex, ConcurrentDictionary<string, FileTransfer>> _currentUploads = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, SyncMediator mediator,
        SyncConfigService syncConfigService,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager,
        ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _syncConfigService = syncConfigService;
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            ResetForServer(msg.ServerIndex);
        });
    }

    /// <summary>
    /// Returns all uploads across all servers. Thread-safe aggregate view.
    /// </summary>
    public List<FileTransfer> CurrentUploads => _currentUploads.Values.SelectMany(x => x.Values).ToList();

    /// <summary>
    /// Gets the upload list for a specific server. Returns a new empty list if none exists.
    /// </summary>
    private ConcurrentDictionary<string, FileTransfer> GetServerUploads(ServerIndex serverIndex)
    {
        return _currentUploads.GetOrAdd(serverIndex, _ => new(StringComparer.Ordinal));
    }

    private ConcurrentDictionary<string, DateTime> GetVerifiedHashes(ServerIndex serverIndex)
    {
        return _verifiedUploadedHashesByServer.GetOrAdd(serverIndex, _ => new(StringComparer.Ordinal));
    }

    public async Task DeleteAllFiles(int serverIndex)
    {
        var uri = RequireUriForServer(serverIndex);

        await _orchestrator.SendRequestAsync(serverIndex, HttpMethod.Post, FilesRoutes.ServerFilesDeleteAllFullPath(uri)).ConfigureAwait(false);
    }

    public async Task<List<string>> UploadFiles(int serverIndex, List<string> hashesToUpload, IProgress<string> progress, CancellationToken ct)
    {
        Logger.LogDebug("Trying to upload files");
        var filesPresentLocally = hashesToUpload.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
        var locallyMissingFiles = hashesToUpload.Except(filesPresentLocally, StringComparer.Ordinal).ToList();
        if (locallyMissingFiles.Any())
        {
            return locallyMissingFiles;
        }

        progress.Report($"Starting upload for {filesPresentLocally.Count} files");

        var filesToUpload = await FilesSend(serverIndex, [.. filesPresentLocally], [], ct).ConfigureAwait(false);

        if (filesToUpload.Exists(f => f.IsForbidden))
        {
            return [.. filesToUpload.Where(f => f.IsForbidden).Select(f => f.Hash)];
        }

        Task uploadTask = Task.CompletedTask;
        int i = 1;
        foreach (var file in filesToUpload)
        {
            progress.Report($"Uploading file {i++}/{filesToUpload.Count}. Please wait until the upload is completed.");
            Logger.LogDebug("[{hash}] Compressing", file);
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, ct).ConfigureAwait(false);
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverIndex, data.Item2, file.Hash, false, ct);
            ct.ThrowIfCancellationRequested();
        }

        await uploadTask.ConfigureAwait(false);

        return [];
    }

    public async Task<CharacterData> UploadFiles(ServerIndex serverIndex, CharacterData data, List<UserData> visiblePlayers)
    {
        CancelUpload(serverIndex);

        var tokenSource = new CancellationTokenSource();
        if (!_cancellationTokens.TryAdd(serverIndex, tokenSource))
        {
            Logger.LogError("[{ServerIndex} Failed to add cancellation token, token already present.", serverIndex);
        }
        var uploadToken = tokenSource.Token;
        Logger.LogDebug("Sending Character data {Hash} to service {Url}", data.DataHash.Value, _serverManager.GetServerByIndex(serverIndex).ServerUri);

        HashSet<string> unverifiedUploads = GetUnverifiedFiles(serverIndex, data);
        if (unverifiedUploads.Any())
        {
            await UploadUnverifiedFiles(serverIndex, unverifiedUploads, visiblePlayers, uploadToken).ConfigureAwait(false);
            var serverName = _serverManager.GetServerByIndex(serverIndex).ServerName;
            Logger.LogInformation("Upload complete for {Hash} to {serverName}", data.DataHash.Value, serverName);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => _orchestrator.IsForbidden(i.Hash));
        }

        return data;
    }

    private async Task<List<UploadFileDto>> FilesSend(int serverIndex, List<string> hashes, List<string> uids, CancellationToken ct)
    {
        var uri = RequireUriForServer(serverIndex);
        FilesSendDto filesSendDto = new()
        {
            FileHashes = hashes,
            UIDs = uids
        };
        var response = await _orchestrator.SendRequestAsync(serverIndex, HttpMethod.Post, FilesRoutes.ServerFilesFilesSendFullPath(uri), filesSendDto, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private HashSet<string> GetUnverifiedFiles(ServerIndex serverIndex, CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            var verifiedHashes = GetVerifiedHashes(serverIndex);
            if (!verifiedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                Logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
    }


    private async Task UploadFile(int serverIndex, byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
    {
        var serverName = _serverManager.GetServerByIndex(serverIndex).ServerName;
        Logger.LogInformation("[{hash}] Uploading {size} to {serverName}", fileHash, UiSharedService.ByteToString(compressedFile.Length), serverName);

        if (uploadToken.IsCancellationRequested) return;

        try
        {
            await UploadFileStream(serverIndex, compressedFile, fileHash, _syncConfigService.Current.UseAlternativeFileUpload, postProgress, uploadToken).ConfigureAwait(false);
            GetVerifiedHashes(serverIndex)[fileHash] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            if (!_syncConfigService.Current.UseAlternativeFileUpload && ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "[{hash}] Error during file upload, trying alternative file upload", fileHash);
                await UploadFileStream(serverIndex, compressedFile, fileHash, munged: true, postProgress, uploadToken).ConfigureAwait(false);
            }
            else
            {
                Logger.LogWarning(ex, "[{hash}] File upload cancelled", fileHash);
            }
        }
    }

    private async Task UploadFileStream(int serverIndex, byte[] compressedFile, string fileHash, bool munged, bool postProgress, CancellationToken uploadToken)
    {
        var uri = RequireUriForServer(serverIndex);
        if (munged)
        {
            FileDownloadManager.MungeBuffer(compressedFile.AsSpan());
        }

        using var ms = new MemoryStream(compressedFile);

        Progress<UploadProgress>? prog = !postProgress ? null : new((prog) =>
        {
            try
            {
                var serverUploads = GetServerUploads(serverIndex);
                if (serverUploads.TryGetValue(fileHash, out var transfer))
                {
                    transfer.Transferred = prog.Uploaded;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[{hash}] Could not set upload progress", fileHash);
            }
        });

        var streamContent = new ProgressableStreamContent(ms, prog);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage response;
        if (!munged)
            response = await _orchestrator.SendRequestStreamAsync(serverIndex, HttpMethod.Post, FilesRoutes.ServerFilesUploadFullPath(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        else
            response = await _orchestrator.SendRequestStreamAsync(serverIndex, HttpMethod.Post, FilesRoutes.ServerFilesUploadMunged(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        Logger.LogDebug("[{hash}] Upload Status: {status}", fileHash, response.StatusCode);
    }

    private async Task UploadUnverifiedFiles(int serverIndex, HashSet<string> unverifiedUploadHashes, List<UserData> visiblePlayers, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        Logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend(serverIndex, [.. unverifiedUploadHashes], visiblePlayers.Select(p => p.UID).Distinct(StringComparer.Ordinal).ToList(), uploadToken).ConfigureAwait(false);

        var serverUploads = GetServerUploads(serverIndex);
        foreach (var file in filesToUpload.Where(f => !f.IsForbidden).DistinctBy(f => f.Hash))
        {
            try
            {
                var transfer = new UploadFileTransfer(file, serverIndex)
                {
                    Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length,
                };
                serverUploads.TryAdd(file.Hash, transfer);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Tried to request file {hash} but file was not present", file.Hash);
            }
        }

        foreach (var file in filesToUpload.Where(c => c.IsForbidden))
        {
            _orchestrator.TryAddForbiddenTransfer(new UploadFileTransfer(file, serverIndex)
            {
                LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
            });

            GetVerifiedHashes(serverIndex)[file.Hash] = DateTime.UtcNow;
        }

        var totalSize = serverUploads.Values.Sum(c => c.Total);
        Logger.LogDebug("Compressing and uploading files");
        Task uploadTask = Task.CompletedTask;
        foreach (var file in serverUploads.Values.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            Logger.LogDebug("[{hash}] Compressing", file);
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            if (serverUploads.TryGetValue(data.Item1, out var uploadTransfer))
            {
                uploadTransfer.Total = data.Item2.Length;
            }
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverIndex, data.Item2, file.Hash, true, uploadToken);
            uploadToken.ThrowIfCancellationRequested();
        }

        if (serverUploads.Count > 0)
        {
            await uploadTask.ConfigureAwait(false);

            var compressedSize = serverUploads.Values.Sum(c => c.Total);
            var serverName = _serverManager.GetServerByIndex(serverIndex).ServerName;
            Logger.LogDebug("Upload complete to {serverName}, compressed {size} to {compressed}", serverName, UiSharedService.ByteToString(totalSize), UiSharedService.ByteToString(compressedSize));
        }

        foreach (var file in unverifiedUploadHashes.Where(c => !serverUploads.ContainsKey(c)))
        {
            GetVerifiedHashes(serverIndex)[file] = DateTime.UtcNow;
        }

        _currentUploads.TryRemove(serverIndex, out _);
    }

    private void CancelUpload(ServerIndex serverIndex)
    {
        CancelUploadsToServer(serverIndex);
    }

    private void CancelUploadsToServer(ServerIndex serverIndex)
    {
        if (_cancellationTokens.TryRemove(serverIndex, out var token))
        {
            token.Cancel();
            token.Dispose();
        }
        _currentUploads.TryRemove(serverIndex, out _);
    }

    private Uri RequireUriForServer(int serverIndex)
    {
        var uri = _orchestrator.GetFileCdnUri(serverIndex);
        if (uri == null) throw new InvalidOperationException("FileTransferManager is not initialized");
        return uri;
    }

    private void ResetForServer(ServerIndex serverIndex)
    {
        CancelUploadsToServer(serverIndex);
        _verifiedUploadedHashesByServer.TryRemove(serverIndex, out _);
    }

    private void Reset()
    {
        _cancellationTokens.Values.ToList().ForEach(c =>
        {
            c.Cancel();
            c.Dispose();
        });
        _cancellationTokens.Clear();
        _currentUploads.Clear();
        _verifiedUploadedHashesByServer.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Reset();
    }
}