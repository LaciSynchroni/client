using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;

namespace LaciSynchroni.WebAPI.Files;

using ServerIndex = int;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private readonly ConcurrentDictionary<ServerIndex, Uri> _cdnUris = new();
    private readonly HttpClient _httpClient;
    private readonly SyncConfigService _syncConfig;
    private readonly Lock _semaphoreModificationLock = new();
    private readonly MultiConnectTokenService _multiConnectTokenService;
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;
    private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

    public List<FileTransfer> ForbiddenTransfers { get; } = [];
    public HttpRequestHeaders DefaultRequestHeaders => _httpClient.DefaultRequestHeaders;

    public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, SyncConfigService syncConfig,
        SyncMediator mediator, HttpClient httpClient, MultiConnectTokenService multiConnectTokenService) : base(logger, mediator)
    {
        _syncConfig = syncConfig;
        _httpClient = httpClient;
        _multiConnectTokenService = multiConnectTokenService;
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        var versionString = string.Create(CultureInfo.InvariantCulture, $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaciSynchroni", versionString));

        _availableDownloadSlots = syncConfig.Current.ParallelDownloads;
        _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            var newUri = msg.Connection.ServerInfo.FileServerAddress;
            _cdnUris.AddOrUpdate(msg.serverIndex, i => newUri, (i, uri) => newUri);
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            _cdnUris.TryRemove(msg.ServerIndex, out _);
        });
        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            _downloadReady[msg.RequestId] = true;
        });
    }

    private readonly ConcurrentDictionary<string, FileTransfer> _forbiddenTransfers = new(StringComparer.Ordinal);

    public List<FileTransfer> ForbiddenTransfers => _forbiddenTransfers.Values.ToList();

    public bool IsForbidden(string hash) => _forbiddenTransfers.ContainsKey(hash);

    public bool TryAddForbiddenTransfer(FileTransfer transfer) => _forbiddenTransfers.TryAdd(transfer.Hash, transfer);

    public Uri? GetFileCdnUri(int serverIndex)
    {
        _cdnUris.TryGetValue(serverIndex, out var uri);
        return uri;
    }

    public void ClearDownloadRequest(Guid guid)
    {
        _downloadReady.Remove(guid, out _);
    }

    public bool IsDownloadReady(Guid guid)
    {
        if (_downloadReady.TryGetValue(guid, out bool isReady) && isReady)
        {
            return true;
        }

        return false;
    }

    public void ReleaseDownloadSlot()
    {
        try
        {
            _downloadSemaphore.Release();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    public async Task<HttpResponseMessage> SendRequestAsync(int serverIndex, HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, bool withAuthToken = true)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(serverIndex, requestMessage, ct, httpCompletionOption, withAuthToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestAsync<T>(int serverIndex, HttpMethod method, Uri uri, T content, CancellationToken ct, bool withAuthToken = true) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content is not ByteArrayContent)
            requestMessage.Content = JsonContent.Create(content);
        else
            requestMessage.Content = content as ByteArrayContent;
        return await SendRequestInternalAsync(serverIndex, requestMessage, ct, withAuthToken: withAuthToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestStreamAsync(int serverIndex, HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct, bool withAuthToken = true)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(serverIndex, requestMessage, ct, withAuthToken: withAuthToken).ConfigureAwait(false);
    }

    public async Task WaitForDownloadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _syncConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _syncConfig.Current.ParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
            }
        }

        await _downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
        Mediator.Publish(new DownloadLimitChangedMessage());
    }

    public long DownloadLimitPerSlot()
    {
        var limit = _syncConfig.Current.DownloadSpeedLimitInBytes;
        if (limit <= 0) return 0;
        limit = _syncConfig.Current.DownloadSpeedType switch
        {
            DownloadSpeeds.Bps => limit,
            DownloadSpeeds.KBps => limit * 1024,
            DownloadSpeeds.MBps => limit * 1024 * 1024,
            _ => limit,
        };
        var currentUsedDlSlots = CurrentlyUsedDownloadSlots;
        var avaialble = _availableDownloadSlots;
        var currentCount = _downloadSemaphore.CurrentCount;
        var dividedLimit = limit / (currentUsedDlSlots == 0 ? 1 : currentUsedDlSlots);
        if (dividedLimit < 0)
        {
            Logger.LogWarning("Calculated Bandwidth Limit is negative, returning Infinity: {Value}, CurrentlyUsedDownloadSlots is {CurrentSlots}, " +
                "DownloadSpeedLimit is {Limit}, available slots: {Avail}, current count: {Count}", dividedLimit, currentUsedDlSlots, limit, avaialble, currentCount);
            return long.MaxValue;
        }
        return Math.Clamp(dividedLimit, 1, long.MaxValue);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(ServerIndex serverIndex, HttpRequestMessage requestMessage,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, bool withAuthToken = true)
    {
        if (withAuthToken)
        {
            var token = await _multiConnectTokenService.GetCachedToken(serverIndex).ConfigureAwait(false);
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent && requestMessage.Content is not ByteArrayContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Sending {Method} to {Uri} (Content: {Content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            Logger.LogDebug("Sending {Method} to {Uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during SendRequestInternal for {Uri}", requestMessage.RequestUri);
            throw;
        }
    }
}