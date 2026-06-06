using Dalamud.Game.ClientState.JobGauge.Enums;
using K4os.Compression.LZ4.Legacy;
using LaciSynchroni.Interop.Ipc;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;

namespace LaciSynchroni.FileCache;

public sealed class FileCacheManager : IHostedService
{
    public const string CachePrefix = "{cache}";
    public const string CsvSplit = "|";
    public const string PenumbraPrefix = "{penumbra}";
    private readonly SyncConfigService _configService;
    private readonly SyncMediator _syncMediator;
    private readonly string _csvPath;
    private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _sha1FileCaches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _blake3FileCaches = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _getCachesByPathsSemaphore = new(1, 1);
    private readonly Lock _fileWriteLock = new();
    private readonly IpcManager _ipcManager;
    private readonly ILogger<FileCacheManager> _logger;
    private readonly List<FileCacheEntity> _blake3HashesRequired = new();
    public string CacheFolder => _configService.Current.CacheFolder;

    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, SyncConfigService configService, SyncMediator syncMediator)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _syncMediator = syncMediator;
        _csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");
    }

    private string CsvBakPath => _csvPath + ".bak";

    public FileCacheEntity? CreateCacheEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace("Creating cache entry for {Path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), CachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace("Creating file entry for {Path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), PenumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public List<FileCacheEntity> GetAllFileCaches() => _sha1FileCaches.Values.SelectMany(v => v).ToList();

    public ConcurrentDictionary<string, List<FileCacheEntity>> GetFileCachesForHash(string hash)
    {
        var len = hash.Length;
        if (len == 40)
        {
            return _sha1FileCaches;
        }
        else if (len == 64)
        {
            return _blake3FileCaches;
        }
        else
        {
            _logger.LogError("Unknown hash type, length {0}", len);
            return new();
        }
    }

    public List<FileCacheEntity> GetAllFileCachesByHash(string hash, bool ignoreCacheEntries = false, bool validate = true)
    {
        List<FileCacheEntity> output = [];
        var _fileCaches = GetFileCachesForHash(hash);

        if (_fileCaches.TryGetValue(hash, out var fileCacheEntities))
        {
            foreach (var fileCache in fileCacheEntities.Where(c => !ignoreCacheEntries || !c.IsCacheEntry).ToList())
            {
                if (!validate) output.Add(fileCache);
                else
                {
                    var validated = GetValidatedFileCache(fileCache);
                    if (validated != null) output.Add(validated);
                }
            }
        }

        return output;
    }

    public Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
    {
        _syncMediator.Publish(new HaltScanMessage(nameof(ValidateLocalIntegrity)));
        _logger.LogInformation("Validating local storage");
        var cacheEntries = _sha1FileCaches.SelectMany(v => v.Value).Where(v => v.IsCacheEntry).ToList();
        List<FileCacheEntity> brokenEntities = [];
        int i = 0;
        foreach (var fileCache in cacheEntries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger.LogInformation("Validating {File}", fileCache.ResolvedFilepath);

            progress.Report((i, cacheEntries.Count, fileCache));
            i++;
            if (!File.Exists(fileCache.ResolvedFilepath))
            {
                brokenEntities.Add(fileCache);
                continue;
            }

            try
            {
                var computedHash = Crypto.GetSHA1FileHash(fileCache.ResolvedFilepath);
                if (!string.Equals(computedHash, fileCache.Sha1Hash, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Failed to validate {File}, got hash {ActualHash}, expected hash {ExpectedHash}", fileCache.ResolvedFilepath, computedHash, fileCache.Sha1Hash);
                    brokenEntities.Add(fileCache);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error during validation of {File}", fileCache.ResolvedFilepath);
                brokenEntities.Add(fileCache);
            }
        }

        foreach (var brokenEntity in brokenEntities)
        {
            RemoveHashedFile(brokenEntity.Sha1Hash, brokenEntity.Blake3Hash, brokenEntity.PrefixedFilePath);

            try
            {
                File.Delete(brokenEntity.ResolvedFilepath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {File}", brokenEntity.ResolvedFilepath);
            }
        }

        _syncMediator.Publish(new ResumeScanMessage(nameof(ValidateLocalIntegrity)));
        return Task.FromResult(brokenEntities);
    }

    public string GetCacheFilePath(string hash, string extension)
    {
        return Path.Combine(_configService.Current.CacheFolder, hash + "." + extension);
    }

    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Wrapper.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        var _fileCaches = GetFileCachesForHash(hash);
        if (_fileCaches.TryGetValue(hash, out var hashes))
        {
            var item = hashes.OrderBy(p => p.PrefixedFilePath.Contains(PenumbraPrefix) ? 0 : 1).FirstOrDefault();
            if (item != null) return GetValidatedFileCache(item);
        }
        return null;
    }

    private FileCacheEntity? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()
            .Replace(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);
        var entry = _sha1FileCaches.SelectMany(v => v.Value).FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            _logger.LogDebug("Found no entries for {Path}", cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public Dictionary<string, FileCacheEntity?> GetFileCachesByPaths(string[] paths)
    {
        _getCachesByPathsSemaphore.Wait();

        try
        {
            var cleanedPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p,
                p => p.Replace("/", "\\", StringComparison.OrdinalIgnoreCase)
                    .Replace(_ipcManager.Penumbra.ModDirectory!, _ipcManager.Penumbra.ModDirectory!.EndsWith('\\') ? PenumbraPrefix + '\\' : PenumbraPrefix, StringComparison.OrdinalIgnoreCase)
                    .Replace(_configService.Current.CacheFolder, _configService.Current.CacheFolder.EndsWith('\\') ? CachePrefix + '\\' : CachePrefix, StringComparison.OrdinalIgnoreCase)
                    .Replace("\\\\", "\\", StringComparison.Ordinal),
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, FileCacheEntity?> result = new(StringComparer.OrdinalIgnoreCase);

            var dict = _sha1FileCaches.SelectMany(f => f.Value)
                .ToDictionary(d => d.PrefixedFilePath, d => d, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cleanedPaths)
            {
                //_logger.LogDebug("Checking {path}", entry.Value);

                if (dict.TryGetValue(entry.Value, out var entity))
                {
                    var validatedCache = GetValidatedFileCache(entity);
                    result.Add(entry.Key, validatedCache);
                }
                else
                {
                    if (!entry.Value.Contains(CachePrefix, StringComparison.Ordinal))
                        result.Add(entry.Key, CreateFileEntry(entry.Key));
                    else
                        result.Add(entry.Key, CreateCacheEntry(entry.Key));
                }
            }

            return result;
        }
        finally
        {
            _getCachesByPathsSemaphore.Release();
        }
    }

    public void RemoveHashedFile(string sha1Hash, string blake3Hash, string prefixedFilePath)
    {
        if (_sha1FileCaches.TryGetValue(sha1Hash, out var caches))
        {
            var removedCount = caches?.RemoveAll(c => string.Equals(c.PrefixedFilePath, prefixedFilePath, StringComparison.Ordinal));
            _logger.LogTrace("Removed from DB: {count} file(s) with hash {hash} and file cache {path}", removedCount, sha1Hash, prefixedFilePath);

            if (caches?.Count == 0)
            {
                _sha1FileCaches.Remove(sha1Hash, out var entity);
            }
        }

        if (_blake3FileCaches.TryGetValue(blake3Hash, out caches))
        {
            var removedCount = caches?.RemoveAll(c => string.Equals(c.PrefixedFilePath, prefixedFilePath, StringComparison.Ordinal));
            _logger.LogTrace("Removed from DB: {count} file(s) with hash {hash} and file cache {path}", removedCount, blake3Hash, prefixedFilePath);

            if (caches?.Count == 0)
            {
                _sha1FileCaches.Remove(blake3Hash, out var entity);
            }
        }
    }

    public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
    {
        _logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);
        var oldSha1Hash = fileCache.Sha1Hash;
        var oldBlake3Hash = fileCache.Blake3Hash;
        var prefixedPath = fileCache.PrefixedFilePath;
        if (computeProperties)
        {
            var fi = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Size = fi.Length;
            fileCache.CompressedSize = null;
            fileCache.Sha1Hash = Crypto.GetSHA1FileHash(fileCache.ResolvedFilepath);
            fileCache.Blake3Hash = Crypto.GetBlake3FileHash(fileCache.ResolvedFilepath);
            fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        RemoveHashedFile(oldSha1Hash, oldBlake3Hash, prefixedPath);
        AddHashedFile(fileCache);
    }

    public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }
        if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    public void WriteOutFullCsv()
    {
        lock (_fileWriteLock)
        {
            StringBuilder sb = new();
            foreach (var entry in _sha1FileCaches.SelectMany(k => k.Value).OrderBy(f => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(entry.CsvEntry);
            }

            if (File.Exists(_csvPath))
            {
                File.Copy(_csvPath, CsvBakPath, overwrite: true);
            }

            try
            {
                File.WriteAllText(_csvPath, sb.ToString());
                File.Delete(CsvBakPath);
            }
            catch
            {
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        try
        {
            RemoveHashedFile(fileCache.Sha1Hash, fileCache.Blake3Hash, fileCache.PrefixedFilePath);
            var extensionPath = fileCache.ResolvedFilepath.ToUpper(CultureInfo.InvariantCulture) + "." + ext;
            File.Move(fileCache.ResolvedFilepath, extensionPath, overwrite: true);
            var newHashedEntity = new FileCacheEntity(fileCache.Sha1Hash, fileCache.Blake3Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            newHashedEntity.SetResolvedFilePath(extensionPath);
            AddHashedFile(newHashedEntity);
            _logger.LogTrace("Migrated from {oldPath} to {newPath}", fileCache.ResolvedFilepath, newHashedEntity.ResolvedFilepath);
            return newHashedEntity;
        }
        catch (Exception ex)
        {
            AddHashedFile(fileCache);
            _logger.LogWarning(ex, "Failed to migrate entity {entity}", fileCache.PrefixedFilePath);
            return fileCache;
        }
    }

    private void AddHashedFile(FileCacheEntity fileCache)
    {
        if (!_sha1FileCaches.TryGetValue(fileCache.Sha1Hash, out var entries) || entries is null)
        {
            _sha1FileCaches[fileCache.Sha1Hash] = entries = [];
        }

        if (!entries.Exists(u => string.Equals(u.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
        {
            //_logger.LogTrace("Adding to DB: {hash} => {path}", fileCache.Hash, fileCache.PrefixedFilePath);
            entries.Add(fileCache);
        }

        if (!_blake3FileCaches.TryGetValue(fileCache.Blake3Hash, out entries) || entries is null)
        {
            _blake3FileCaches[fileCache.Blake3Hash] = entries = [];
        }

        if (!entries.Exists(u => string.Equals(u.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
        {
            //_logger.LogTrace("Adding to DB: {hash} => {path}", fileCache.Hash, fileCache.PrefixedFilePath);
            entries.Add(fileCache);
        }

    }

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? sha1Hash = null, string? blake3Hash = null)
    {
        sha1Hash ??= Crypto.GetSHA1FileHash(fileInfo.FullName);
        blake3Hash ??= Crypto.GetBlake3FileHash(fileInfo.FullName);
        var entity = new FileCacheEntity(sha1Hash, blake3Hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
        entity = ReplacePathPrefixes(entity);
        AddHashedFile(entity);
        lock (_fileWriteLock)
        {
            File.AppendAllLines(_csvPath, new[] { entity.CsvEntry });
        }
        var result = GetFileCacheByPath(fileInfo.FullName);
        _logger.LogTrace("Creating cache entity for {name} success: {success}", fileInfo.FullName, (result != null));
        return result;
    }

    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        var resultingFileCache = ReplacePathPrefixes(fileCache);
        //_logger.LogTrace("Validating {path}", fileCache.PrefixedFilePath);
        resultingFileCache = Validate(resultingFileCache);
        return resultingFileCache;
    }

    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.Penumbra.ModDirectory, StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configService.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            RemoveHashedFile(fileCache.Sha1Hash, fileCache.Blake3Hash, fileCache.PrefixedFilePath);
            return null;
        }

        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            UpdateHashedFile(fileCache);
        }

        return fileCache;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting FileCacheManager");

        lock (_fileWriteLock)
        {
            try
            {
                _logger.LogInformation("Checking for {bakPath}", CsvBakPath);

                if (File.Exists(CsvBakPath))
                {
                    _logger.LogInformation("{bakPath} found, moving to {csvPath}", CsvBakPath, _csvPath);

                    File.Move(CsvBakPath, _csvPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
                try
                {
                    if (File.Exists(CsvBakPath))
                        File.Delete(CsvBakPath);
                }
                catch (Exception ex1)
                {
                    _logger.LogWarning(ex1, "Could not delete bak file");
                }
            }
        }

        if (File.Exists(_csvPath))
        {
            if (!_ipcManager.Penumbra.APIAvailable || string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
            {
                _syncMediator.Publish(new NotificationMessage("Penumbra not connected",
                    "Could not load local file cache data. Penumbra is not connected or not properly set up." +
                    " Please enable and/or configure Penumbra properly to use this plugin. After, reload the plugin in the Plugin installer.",
                    NotificationType.Error));
            }

            _logger.LogInformation("{CsvPath} found, parsing", _csvPath);

            bool success = false;
            string[] entries = [];
            int attempts = 0;
            while (!success && attempts < 10)
            {
                try
                {
                    _logger.LogInformation("Attempting to read {CsvPath}", _csvPath);
                    entries = File.ReadAllLines(_csvPath);
                    success = true;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.LogWarning(ex, "Could not open {File}, trying again", _csvPath);
                    Thread.Sleep(100);
                }
            }

            if (entries.Length == 0)
            {
                _logger.LogWarning("Could not load entries from {Path}, continuing with empty file cache", _csvPath);
            }

            _logger.LogInformation("Found {Amount} files in {Path}", entries.Length, _csvPath);

            Dictionary<string, bool> processedFiles = new(StringComparer.OrdinalIgnoreCase);

            var cacheNeedsMigration = false;

            foreach (var entry in entries)
            {
                var splittedEntry = entry.Split(CsvSplit, StringSplitOptions.None);
                var isUnmigratedEntry = false;
                if (splittedEntry.Length == 5)
                {
                    cacheNeedsMigration = true;
                    isUnmigratedEntry = true;
                }
                try
                {
                    var sha1Hash = splittedEntry[0];
                    if (sha1Hash.Length != 40) throw new InvalidOperationException("Expected Hash length of 40, received " + sha1Hash.Length);
                    var blake3Hash = isUnmigratedEntry ? "" : splittedEntry[5];
                    // If the len is 0, it's either unmigrated or simply hasn't been generated yet, so no point crashing out here, but if it exists, it's corrupt.
                    if (blake3Hash.Length == 0) _logger.LogWarning("Expected Hash length of 64, received {0}, marking file for rehash", blake3Hash.Length);
                    else if (blake3Hash.Length != 64) throw new InvalidOperationException("Expected Hash length of 64, hash is not empty, received " + blake3Hash.Length);
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];

                    if (processedFiles.ContainsKey(path))
                    {
                        _logger.LogWarning("Already processed {file}, ignoring", path);
                        continue;
                    }

                    processedFiles.Add(path, value: true);

                    long size = -1;
                    long compressed = -1;
                    if (splittedEntry.Length > 3)
                    {
                        if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out long result))
                        {
                            size = result;
                        }
                        if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out long resultCompressed))
                        {
                            compressed = resultCompressed;
                        }
                    }
                    var file = ReplacePathPrefixes(new FileCacheEntity(sha1Hash, blake3Hash, path, time, size, compressed));
                    if (isUnmigratedEntry)
                    {
                        _blake3HashesRequired.Add(file);
                    }
                    AddHashedFile(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize entry {entry}, ignoring", entry);
                }
            }

            if (processedFiles.Count != entries.Length || cacheNeedsMigration)
            {
                WriteOutFullCsv();
            }
        }

        _logger.LogInformation("Started FileCacheManager");

        if (_blake3HashesRequired.Count > 0)
        {
            var thread = new Thread(UpdateBlake3Hashes);
            thread.Start(_blake3HashesRequired);
        }

        return Task.CompletedTask;
    }

    private void UpdateBlake3Hashes(object? entities)
    {    
        if (entities is null)
        {
            return;
        }
        var entitiesList = (List<FileCacheEntity>)entities;
        var count = 0;
        foreach (var entity in entitiesList)
        {
            entity.Blake3Hash = Crypto.GetBlake3FileHash(entity.ResolvedFilepath);

            if (!_blake3FileCaches.TryGetValue(entity.Blake3Hash, out var entries) || entries is null)
            {
                _blake3FileCaches[entity.Blake3Hash] = entries = [];
            }

            if (!entries.Exists(u => string.Equals(u.PrefixedFilePath, entity.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                //_logger.LogTrace("Adding to DB: {hash} => {path}", fileCache.Hash, fileCache.PrefixedFilePath);
                entries.Add(entity);
            }
            count++;
            if (count == 25)
            {
                WriteOutFullCsv();
                count = 0;
            }
        }
        WriteOutFullCsv();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        WriteOutFullCsv();
        return Task.CompletedTask;
    }
}