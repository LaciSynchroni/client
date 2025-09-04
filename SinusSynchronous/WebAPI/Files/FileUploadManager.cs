using Microsoft.Extensions.Logging;
using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto.Files;
using SinusSynchronous.API.Routes;
using SinusSynchronous.FileCache;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.Services.ServerConfiguration;
using SinusSynchronous.SinusConfiguration;
using SinusSynchronous.UI;
using SinusSynchronous.WebAPI.Files.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SinusSynchronous.WebAPI.Files;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileDbManager;
    private readonly SinusConfigService _sinusConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _uploadCancellationTokenSource = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, SinusMediator mediator,
        SinusConfigService sinusConfigService,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager,
        ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _sinusConfigService = sinusConfigService;
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        // TODO based on server index
        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            Reset();
        });
    }

    public List<FileTransfer> CurrentUploads { get; } = [];
    public bool IsUploading => CurrentUploads.Count > 0;

    public async Task DeleteAllFiles(int serverIndex)
    {
        var uri = RequireUriForServer(serverIndex);

        await _orchestrator.SendRequestAsync(serverIndex, HttpMethod.Post, SinusFiles.ServerFilesDeleteAllFullPath(uri)).ConfigureAwait(false);
    }

    public async Task<List<string>> UploadFiles(int serverIndex, List<string> hashesToUpload, IProgress<string> progress, CancellationToken? ct = null)
    {
        Logger.LogDebug("Trying to upload files");
        var filesPresentLocally = hashesToUpload.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
        var locallyMissingFiles = hashesToUpload.Except(filesPresentLocally, StringComparer.Ordinal).ToList();
        if (locallyMissingFiles.Any())
        {
            return locallyMissingFiles;
        }

        progress.Report($"Starting upload for {filesPresentLocally.Count} files");

        var filesToUpload = await FilesSend(serverIndex, [.. filesPresentLocally], [], ct ?? CancellationToken.None).ConfigureAwait(false);

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
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, ct ?? CancellationToken.None).ConfigureAwait(false);
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverIndex, data.Item2, file.Hash, false, ct ?? CancellationToken.None);
            (ct ?? CancellationToken.None).ThrowIfCancellationRequested();
        }

        await uploadTask.ConfigureAwait(false);

        return [];
    }

    public async Task<CharacterData> UploadFiles(int serverIndex, CharacterData data, List<UserData> visiblePlayers)
    {
        CancelUpload();

        _uploadCancellationTokenSource = new CancellationTokenSource();
        var uploadToken = _uploadCancellationTokenSource.Token;
        Logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);

        HashSet<string> unverifiedUploads = GetUnverifiedFiles(data);
        if (unverifiedUploads.Any())
        {
            await UploadUnverifiedFiles(serverIndex, unverifiedUploads, visiblePlayers, uploadToken).ConfigureAwait(false);
            Logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => _orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }

        return data;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Reset();
    }

    private async Task<List<UploadFileDto>> FilesSend(int serverIndex, List<string> hashes, List<string> uids, CancellationToken ct)
    {
        var uri = RequireUriForServer(serverIndex);
        FilesSendDto filesSendDto = new()
        {
            FileHashes = hashes,
            UIDs = uids
        };
        var response = await _orchestrator.SendRequestAsync(serverIndex, HttpMethod.Post, SinusFiles.ServerFilesFilesSendFullPath(uri), filesSendDto, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private HashSet<string> GetUnverifiedFiles(CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
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

    private void Reset()
    {
        _uploadCancellationTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Dispose();
        _uploadCancellationTokenSource = null;
        CurrentUploads.Clear();
        _verifiedUploadedHashes.Clear();
    }

    private async Task UploadFile(int serverIndex, byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
    {
        Logger.LogInformation("[{hash}] Uploading {size}", fileHash, UiSharedService.ByteToString(compressedFile.Length));

        if (uploadToken.IsCancellationRequested) return;

        try
        {
            await UploadFileStream(serverIndex, compressedFile, fileHash, _sinusConfigService.Current.UseAlternativeFileUpload, postProgress, uploadToken).ConfigureAwait(false);
            _verifiedUploadedHashes[fileHash] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            if (!_sinusConfigService.Current.UseAlternativeFileUpload && ex is not OperationCanceledException)
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
                CurrentUploads.Single(f => string.Equals(f.Hash, fileHash, StringComparison.Ordinal) && f.ServerIndex == serverIndex).Transferred = prog.Uploaded;
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
            response = await _orchestrator.SendRequestStreamAsync(serverIndex, HttpMethod.Post, SinusFiles.ServerFilesUploadFullPath(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        else
            response = await _orchestrator.SendRequestStreamAsync(serverIndex, HttpMethod.Post, SinusFiles.ServerFilesUploadMunged(uri, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        Logger.LogDebug("[{hash}] Upload Status: {status}", fileHash, response.StatusCode);
    }

    private async Task UploadUnverifiedFiles(int serverIndex, HashSet<string> unverifiedUploadHashes, List<UserData> visiblePlayers, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        Logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend(serverIndex, [.. unverifiedUploadHashes], visiblePlayers.Select(p => p.UID).ToList(), uploadToken).ConfigureAwait(false);

        foreach (var file in filesToUpload.Where(f => !f.IsForbidden).DistinctBy(f => f.Hash))
        {
            try
            {
                CurrentUploads.Add(new UploadFileTransfer(file, serverIndex)
                {
                    Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length,
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Tried to request file {hash} but file was not present", file.Hash);
            }
        }

        foreach (var file in filesToUpload.Where(c => c.IsForbidden))
        {
            if (_orchestrator.ForbiddenTransfers.TrueForAll(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new UploadFileTransfer(file, serverIndex)
                {
                    LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
                });
            }

            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
        }

        var totalSize = CurrentUploads.Sum(c => c.Total);
        Logger.LogDebug("Compressing and uploading files");
        Task uploadTask = Task.CompletedTask;
        foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            Logger.LogDebug("[{hash}] Compressing", file);
            var data = await _fileDbManager.GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal) && e.ServerIndex == serverIndex).Total = data.Item2.Length;
            Logger.LogDebug("[{hash}] Starting upload for {filePath}", data.Item1, _fileDbManager.GetFileCacheByHash(data.Item1)!.ResolvedFilepath);
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(serverIndex, data.Item2, file.Hash, true, uploadToken);
            uploadToken.ThrowIfCancellationRequested();
        }

        if (CurrentUploads.Any())
        {
            await uploadTask.ConfigureAwait(false);

            var compressedSize = CurrentUploads.Sum(c => c.Total);
            Logger.LogDebug("Upload complete, compressed {size} to {compressed}", UiSharedService.ByteToString(totalSize), UiSharedService.ByteToString(compressedSize));
        }

        foreach (var file in unverifiedUploadHashes.Where(c => !CurrentUploads.Exists(u => string.Equals(u.Hash, c, StringComparison.Ordinal) && u.ServerIndex == serverIndex)))
        {
            _verifiedUploadedHashes[file] = DateTime.UtcNow;
        }

        CurrentUploads.Clear();
    }
    
    private bool CancelUpload()
    {
        if (CurrentUploads.Any())
        {
            Logger.LogDebug("Cancelling current upload");
            _uploadCancellationTokenSource?.Cancel();
            _uploadCancellationTokenSource?.Dispose();
            _uploadCancellationTokenSource = null;
            CurrentUploads.Clear();
            return true;
        }

        return false;
    }

    private Uri RequireUriForServer(int serverIndex)
    {
        var uri = _orchestrator.GetFileCdnUri(serverIndex);
        if (uri == null) throw new InvalidOperationException("FileTransferManager is not initialized");
        return uri;
    }
}