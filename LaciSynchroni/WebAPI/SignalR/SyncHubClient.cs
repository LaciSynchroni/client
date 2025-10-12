using Dalamud.Utility;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Data.Extensions;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.Common.Dto.User;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Events;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI.SignalR;
using LaciSynchroni.WebAPI.SignalR.Utils;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace LaciSynchroni.WebAPI;

public partial class SyncHubClient : DisposableMediatorSubscriberBase, IServerHubClient
{
    /// <summary>
    /// Index of the server we are currently connected to, <see cref="LaciSynchroni.SyncConfiguration.Models.ServerStorage"/>
    /// </summary>
    public readonly int ServerIndex;

    private readonly bool _isWine;

    private readonly ILogger _logger;
    // For testing if we can connect to the hub
    private readonly HttpClient _httpClient;
    private readonly ILoggerProvider _loggerProvider;
    private readonly MultiConnectTokenService _multiConnectTokenService;
    private readonly PairManager _pairManager;
    private readonly SyncConfigService _syncConfigService;
    private readonly DalamudUtilService _dalamudUtil;

    // This is a bit unfortunate, but some code requires saving of the config. Potentially, we can refactor this late to be less cyclic
    // Potentially, we can move _server out of this class and always use _serverConfigurationManager.
    private readonly ServerConfigurationManager _serverConfigurationManager;


    // SignalR hub connection, one is maintained per server
    private HubConnection? _connection;
    private bool _isDisposed = false;
    private bool _initialized;
    private bool _naggedAboutLod = false;
    public ServerState _serverState { get; private set; }
    private CancellationTokenSource? _healthCheckTokenSource = new();
    private CancellationTokenSource? _connectionCancellationTokenSource;
    private bool _doNotNotifyOnNextInfo = false;
    public string AuthFailureMessage { get; private set; } = string.Empty;
    private string? _lastUsedToken;
    private CensusUpdateMessage? _lastCensus;

    public ConnectionDto? ConnectionDto { get; private set; }
    public SystemInfoDto? SystemInfoDto { get; private set; }

    protected bool IsConnected => _serverState == ServerState.Connected;
    public string UID => ConnectionDto?.User.UID ?? string.Empty;

    private ServerStorage ServerToUse => _serverConfigurationManager.GetServerByIndex(ServerIndex);

    public SyncHubClient(int serverIndex,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager,
        DalamudUtilService dalamudUtilService,
        ILoggerFactory loggerFactory, ILoggerProvider loggerProvider, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) : base(
        loggerFactory.CreateLogger(nameof(SyncHubClient) + serverIndex + "Mediator"), mediator)
    {
        ServerIndex = serverIndex;
        _isWine = dalamudUtilService.IsWine;
        _logger = loggerFactory.CreateLogger(nameof(SyncHubClient) + serverIndex);
        _loggerProvider = loggerProvider;
        _multiConnectTokenService = multiConnectTokenService;
        _syncConfigService = syncConfigService;
        _httpClient = httpClient;
        _pairManager = pairManager;
        _dalamudUtil = dalamudUtilService;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<CyclePauseMessage>(this, (msg) => _ = CyclePauseAsync(msg.ServerIndex, msg.UserData));
        Mediator.Subscribe<CensusUpdateMessage>(this, (msg) => _lastCensus = msg);
        Mediator.Subscribe<PauseMessage>(this, (msg) => _ = PauseAsync(msg.ServerIndex, msg.UserData));
    }


    public async Task CreateConnectionsAsync()
    {
        if (!await VerifyCensus().ConfigureAwait(false))
        {
            return;
        }

        // We actually do the "full pause" handling in here
        // This is a candidate for refactoring in the future
        // Full pause = press the disconnect button
        if (!await VerifyFullPause().ConfigureAwait(false))
        {
            return;
        }

        // Since CreateConnectionAsync is a bit of a catch all, we go through a few validations first, see if we need to show any popups
        if (!await VerifySecretKeyAuth().ConfigureAwait(false))
        {
            return;
        }

        if (!await VerifyOAuth().ConfigureAwait(false))
        {
            return;
        }
        
        // TODO: remove this temporary config migration stuff
        // Attempt to find the hub URI by trying possible old/new hubs
        _serverState = ServerState.Connecting;
        var hubUri = await FindHubUrl().ConfigureAwait(false);
        if (hubUri.IsNullOrEmpty())
        {
            await StopConnectionAsync(ServerState.NoHubFound).ConfigureAwait(false);
            return;
        }

        // Just make sure no open connection exists. It shouldn't, but can't hurt (At least I assume that was the intent...)
        // Also set to "Connecting" at this point
        await StopConnectionAsync(ServerState.Connecting).ConfigureAwait(false);
        Logger.LogInformation("Recreating Connection");
        Mediator.Publish(new EventMessage(new Event(nameof(ApiController), EventSeverity.Informational,
            $"Starting Connection to {ServerToUse.ServerName}")));

        // If we have an old token, we clear it out so old tokens won't accidentally cancel the fresh connect attempt
        // This will also cancel out all ongoing connection attempts for this server, if any are still pending
        _connectionCancellationTokenSource?.Cancel();
        _connectionCancellationTokenSource?.Dispose();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var cancelReconnectToken = _connectionCancellationTokenSource.Token;
        // Loop and try to establish connections
        await LoopConnections(hubUri, cancelReconnectToken).ConfigureAwait(false);
    }

    private async Task LoopConnections(String hubUri, CancellationToken cancelReconnectToken)
    {
        while (_serverState is not ServerState.Connected && !cancelReconnectToken.IsCancellationRequested)
        {
            AuthFailureMessage = string.Empty;
            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
            _serverState = ServerState.Connecting;

            try
            {
                await TryConnect(hubUri).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // cancellation token was triggered, either through user input or an error
                // if this is the case, we just log it and leave. Connection wasn't established set, nothing to erase
                _logger.LogWarning("Connection attempt cancelled");
                return;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HttpRequestException on Connection");

                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // we don't want to spam our auth server. Raze down the connection and leave
                    await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(false);
                    return;
                }

                _serverState = ServerState.Reconnecting;
                _logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), cancelReconnectToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogWarning(ex, "InvalidOperationException on connection");
                await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Exception on Connection");
                Logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), cancelReconnectToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task TryConnect(string hubUri)
    {
        _connectionCancellationTokenSource?.Cancel();
        _connectionCancellationTokenSource?.Dispose();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _connectionCancellationTokenSource.Token;
        AuthFailureMessage = string.Empty;
        await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
        _serverState = ServerState.Connecting;

        await UpdateToken(cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _connection = InitializeHubConnection(cancellationToken, hubUri);
        InitializeApiHooks();

        await _connection.StartAsync(cancellationToken).ConfigureAwait(false);

        ConnectionDto = await GetConnectionDto().ConfigureAwait(false);
        if (!await VerifyClientVersion(ConnectionDto).ConfigureAwait(false))
        {
            await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(false);
            return;
        }
        // Also trigger some warnings
        TriggerConnectionWarnings(ConnectionDto);

        _serverState = ServerState.Connected;

        await LoadIninitialPairsAsync().ConfigureAwait(false);
        await LoadOnlinePairsAsync().ConfigureAwait(false);
    }

    private async Task<string?> FindHubUrl()
    {
        var configuredHubUri = ServerToUse.ServerHubUri.Replace("wss://", "https://").Replace("ws://", "http://");
        var baseUri = ServerToUse.ServerUri.Replace("wss://", "https://").Replace("ws://", "http://");
        if (baseUri.EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = baseUri.Remove(baseUri.Length - 1);
        }
        
        // Essentially, we try every hub we are aware of plus the configured hub to see if we can find a connection
        var hubsToCheck = new[] { configuredHubUri, baseUri + IServerHub.Path, baseUri + "/mare" };
        foreach (var hubToCheck in hubsToCheck)
        {
            if (hubToCheck.IsNullOrEmpty())
            {
                continue;
            }
            var resHub = await _httpClient.GetAsync(hubToCheck).ConfigureAwait(false);
            // If we get a 401, 200 or 204 we have found a hub. The last two should not happen, 401 means that the URL is valid and likely a hub connection
            // We could try to emulate the /negotiate, but for now, this should work just as well
            if (resHub.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.OK or HttpStatusCode.NoContent)
            {
                Logger.LogInformation("{HubUri}: Valid hub, using", hubToCheck);
                return hubToCheck;
            }
            Logger.LogWarning("{HubUri}: Not valid, attempting next hub", hubToCheck);
        }

        Logger.LogWarning("Unable to find any hub to connect to, aborting connection attempt.");
        return null;
    }

    private async Task UpdateToken(CancellationToken cancellationToken)
    {
        try
        {
            _lastUsedToken = await _multiConnectTokenService.GetOrUpdateToken(ServerIndex, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncAuthFailureException ex)
        {
            AuthFailureMessage = ex.Reason;
            throw new HttpRequestException("Error during authentication", ex, HttpStatusCode.Unauthorized);
        }
    }

    private async Task StopConnectionAsync(ServerState state)
    {
        _serverState = ServerState.Disconnecting;

        _logger.LogInformation("Stopping existing connection to {ServerName}", ServerToUse.ServerName);

        if (_connection != null && !_isDisposed)
        {
            _logger.LogDebug("Disposing current HubConnection");
            _isDisposed = true;

            _connection.Closed -= ConnectionOnClosed;
            _connection.Reconnecting -= ConnectionOnReconnecting;
            _connection.Reconnected -= ConnectionOnReconnectedAsync;

            await _connection.StopAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);

            Mediator.Publish(new EventMessage(new Event(nameof(ApiController), EventSeverity.Informational,
                $"Stopping existing connection to {ServerToUse.ServerName}")));
            _initialized = false;
            _healthCheckTokenSource?.Cancel();
            Mediator.Publish(new DisconnectedMessage(ServerIndex));
            _connection = null;
            ConnectionDto = null;

            _connection = null;

            Logger.LogDebug("Current HubConnection disposed");
        }

        _serverState = state;
    }

    private HubConnection InitializeHubConnection(CancellationToken ct, string hubUrl)
    {
        var transportType = ServerToUse.HttpTransportType switch
        {
            HttpTransportType.None => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents |
                                      HttpTransportType.LongPolling,
            HttpTransportType.WebSockets => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents |
                                            HttpTransportType.LongPolling,
            HttpTransportType.ServerSentEvents => HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling,
            HttpTransportType.LongPolling => HttpTransportType.LongPolling,
            _ => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling
        };

        if (_isWine && !ServerToUse.ForceWebSockets
                    && transportType.HasFlag(HttpTransportType.WebSockets))
        {
            _logger.LogDebug("Wine detected, falling back to ServerSentEvents / LongPolling");
            transportType = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
        }

        _logger.LogDebug("Building new HubConnection using transport {Transport}", transportType);

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => _multiConnectTokenService.GetOrUpdateToken(ServerIndex, ct);
                options.Transports = transportType;
            })
            .AddMessagePackProtocol(opt =>
            {
                var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                    BuiltinResolver.Instance,
                    AttributeFormatterResolver.Instance,
                    // replace enum resolver
                    DynamicEnumAsStringResolver.Instance,
                    DynamicGenericResolver.Instance,
                    DynamicUnionResolver.Instance,
                    DynamicObjectResolver.Instance,
                    PrimitiveObjectResolver.Instance,
                    // final fallback(last priority)www
                    StandardResolver.Instance);

                opt.SerializerOptions =
                    MessagePackSerializerOptions.Standard
                        .WithCompression(MessagePackCompression.Lz4Block)
                        .WithResolver(resolver);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy(Mediator, ServerIndex))
            .ConfigureLogging(a =>
            {
                a.ClearProviders().AddProvider(_loggerProvider);
                a.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        _connection.Closed += ConnectionOnClosed;
        _connection.Reconnecting += ConnectionOnReconnecting;
        _connection.Reconnected += ConnectionOnReconnectedAsync;

        _isDisposed = false;
        return _connection;
    }

    private Task ConnectionOnClosed(Exception? arg)
    {
        _healthCheckTokenSource?.Cancel();
        Mediator.Publish(new DisconnectedMessage(ServerIndex));
        _serverState = ServerState.Offline;
        if (arg != null)
        {
            _logger.LogWarning(arg, "Connection closed");
        }
        else
        {
            _logger.LogInformation("Connection closed");
        }

        return Task.CompletedTask;
    }

    private async Task ConnectionOnReconnectedAsync(string? arg)
    {
        _serverState = ServerState.Reconnecting;
        try
        {
            InitializeApiHooks();
            ConnectionDto = await GetConnectionDtoAsync(publishConnected: false).ConfigureAwait(false);
            if (ConnectionDto.ServerVersion != IServerHub.ApiVersion && !ServerToUse.BypassVersionCheck)
            {
                await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(false);
                return;
            }

            _serverState = ServerState.Connected;
            await LoadIninitialPairsAsync().ConfigureAwait(false);
            await LoadOnlinePairsAsync().ConfigureAwait(false);
            Mediator.Publish(new ConnectedMessage(ConnectionDto, ServerIndex));
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Failure to obtain data after reconnection");
            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
        }
    }

    private Task ConnectionOnReconnecting(Exception? arg)
    {
        _doNotNotifyOnNextInfo = true;
        _healthCheckTokenSource?.Cancel();
        _serverState = ServerState.Reconnecting;
        Logger.LogWarning(arg, "Connection closed... Reconnecting");
        Mediator.Publish(new EventMessage(new Event(nameof(ApiController), EventSeverity.Warning,
            $"Connection interrupted, reconnecting to {ServerToUse.ServerName}")));
        return Task.CompletedTask;
    }

    public Task<ConnectionDto> GetConnectionDto() => GetConnectionDtoAsync(true);

    public async Task<ConnectionDto> GetConnectionDtoAsync(bool publishConnected)
    {
        var dto = await _connection!.InvokeAsync<ConnectionDto>(nameof(GetConnectionDto)).ConfigureAwait(false);
        if (publishConnected) Mediator.Publish(new ConnectedMessage(dto, ServerIndex));
        return dto;
    }


    private void InitializeApiHooks()
    {
        if (_connection == null) return;

        Logger.LogDebug("Initializing data");
        OnDownloadReady((guid) => _ = Client_DownloadReady(guid));
        OnReceiveServerMessage((sev, msg) => _ = Client_ReceiveServerMessage(sev, msg));
        OnUpdateSystemInfo((dto) => _ = Client_UpdateSystemInfo(dto));

        OnUserSendOffline((dto) => _ = Client_UserSendOffline(dto));
        OnUserAddClientPair((dto) => _ = Client_UserAddClientPair(dto));
        OnUserReceiveCharacterData((dto) => _ = Client_UserReceiveCharacterData(dto));
        OnUserRemoveClientPair(dto => _ = Client_UserRemoveClientPair(dto));
        OnUserSendOnline(dto => _ = Client_UserSendOnline(dto));
        OnUserUpdateOtherPairPermissions(dto => _ = Client_UserUpdateOtherPairPermissions(dto));
        OnUserUpdateSelfPairPermissions(dto => _ = Client_UserUpdateSelfPairPermissions(dto));
        OnUserReceiveUploadStatus(dto => _ = Client_UserReceiveUploadStatus(dto));
        OnUserUpdateProfile(dto => _ = Client_UserUpdateProfile(dto));
        OnUserDefaultPermissionUpdate(dto => _ = Client_UserUpdateDefaultPermissions(dto));
        OnUpdateUserIndividualPairStatusDto(dto => _ = Client_UpdateUserIndividualPairStatusDto(dto));

        OnGroupChangePermissions((dto) => _ = Client_GroupChangePermissions(dto));
        OnGroupDelete((dto) => _ = Client_GroupDelete(dto));
        OnGroupPairChangeUserInfo((dto) => _ = Client_GroupPairChangeUserInfo(dto));
        OnGroupPairJoined((dto) => _ = Client_GroupPairJoined(dto));
        OnGroupPairLeft((dto) => _ = Client_GroupPairLeft(dto));
        OnGroupSendFullInfo((dto) => _ = Client_GroupSendFullInfo(dto));
        OnGroupSendInfo((dto) => _ = Client_GroupSendInfo(dto));
        OnGroupChangeUserPairPermissions((dto) => _ = Client_GroupChangeUserPairPermissions(dto));

        OnGposeLobbyJoin((dto) => _ = Client_GposeLobbyJoin(dto));
        OnGposeLobbyLeave((dto) => _ = Client_GposeLobbyLeave(dto));
        OnGposeLobbyPushCharacterData((dto) => _ = Client_GposeLobbyPushCharacterData(dto));
        OnGposeLobbyPushPoseData((dto, data) => _ = Client_GposeLobbyPushPoseData(dto, data));
        OnGposeLobbyPushWorldData((dto, data) => _ = Client_GposeLobbyPushWorldData(dto, data));

        _healthCheckTokenSource?.Cancel();
        _healthCheckTokenSource?.Dispose();
        _healthCheckTokenSource = new CancellationTokenSource();
        _ = ClientHealthCheckAsync(_healthCheckTokenSource.Token);

        _initialized = true;
    }

    private async Task ClientHealthCheckAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _connection != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            Logger.LogDebug("Checking Client Health State");

            bool requireReconnect = await RefreshTokenAsync(ct).ConfigureAwait(false);

            if (requireReconnect) break;

            _ = await CheckClientHealth().ConfigureAwait(false);
        }
    }

    private async Task<bool> RefreshTokenAsync(CancellationToken ct)
    {
        bool requireReconnect = false;
        try
        {
            var token = await _multiConnectTokenService.GetOrUpdateToken(ServerIndex, ct).ConfigureAwait(false);
            if (!string.Equals(token, _lastUsedToken, StringComparison.Ordinal))
            {
                Logger.LogDebug("Reconnecting due to updated token");

                _doNotNotifyOnNextInfo = true;
                await CreateConnectionsAsync().ConfigureAwait(false);
                requireReconnect = true;
            }
        }
        catch (SyncAuthFailureException ex)
        {
            AuthFailureMessage = ex.Reason;
            await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(false);
            requireReconnect = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not refresh token, forcing reconnect");
            _doNotNotifyOnNextInfo = true;
            await CreateConnectionsAsync().ConfigureAwait(false);
            requireReconnect = true;
        }

        return requireReconnect;
    }

    private async Task LoadIninitialPairsAsync()
    {
        foreach (var entry in await GroupsGetAll().ConfigureAwait(false))
        {
            Logger.LogDebug("Group: {entry}", entry);
            _pairManager.AddGroup(entry, ServerIndex);
        }

        foreach (var userPair in await UserGetPairedClients().ConfigureAwait(false))
        {
            Logger.LogDebug("Individual Pair: {userPair}", userPair);
            _pairManager.AddUserPair(userPair, ServerIndex);
        }
    }

    private async Task LoadOnlinePairsAsync()
    {
        CensusDataDto? dto = null;
        if (_serverConfigurationManager.SendCensusData && _lastCensus != null)
        {
            var world = await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(false);
            dto = new((ushort)world, _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
            Logger.LogDebug("Attaching Census Data: {data}", dto);
        }

        foreach (var entry in await UserGetOnlinePairs(dto).ConfigureAwait(false))
        {
            Logger.LogDebug("Pair online: {pair}", entry);
            _pairManager.MarkPairOnline(entry, ServerIndex, sendNotif: false);
        }
    }

    public async Task DisposeAsync()
    {
        // Important to dispose all subscriptions first, just in case some random event fires that triggers a reconnect or something
        // Make sure to call the override. If you don't, you end up looping between Dispose and DisposeAsync.
        base.Dispose(true);
        _healthCheckTokenSource?.Cancel();
        // Important to call StopConnectionAsync here because we want the proper message publishing of the disconnect message.
        await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
        _connectionCancellationTokenSource?.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        _ = Task.Run(async () => await DisposeAsync().ConfigureAwait(false));
    }

    public async Task<bool> CheckClientHealth()
    {
        return await _connection!.InvokeAsync<bool>(nameof(CheckClientHealth)).ConfigureAwait(false);
    }

    public async Task DalamudUtilOnLogIn()
    {
        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var auth = ServerToUse.Authentications.Find(f =>
            string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
        if (auth?.AutoLogin ?? false)
        {
            Logger.LogInformation("Logging into {chara}", charaName);
            await CreateConnectionsAsync().ConfigureAwait(false);
        }
        else
        {
            Logger.LogInformation("Not logging into {chara}, auto login disabled", charaName);
            await StopConnectionAsync(ServerState.NoAutoLogon).ConfigureAwait(false);
        }
    }

    public Task CyclePauseAsync(int serverIndex, UserData userData)
    {
        if (serverIndex != ServerIndex)
        {
            return Task.CompletedTask;
        }
        CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            var pair = _pairManager.GetOnlineUserPairs(ServerIndex).Single(p => p.UserPair != null && p.UserData == userData);
            var perm = pair.UserPair!.OwnPermissions;
            perm.SetPaused(paused: true);
            await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);
            // wait until it's changed
            while (pair.UserPair!.OwnPermissions != perm)
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                Logger.LogTrace("Waiting for permissions change for {data}", userData);
            }

            perm.SetPaused(paused: false);
            await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);
        }, cts.Token).ContinueWith((t) => cts.Dispose());

        return Task.CompletedTask;
    }

    private async Task PauseAsync(int serverIndex, UserData userData)
    {
        if (serverIndex != ServerIndex)
        {
            return;
        }
        var pair = _pairManager.GetOnlineUserPairs(ServerIndex).Single(p => p.UserPair != null && p.UserData == userData);
        var perm = pair.UserPair!.OwnPermissions;
        perm.SetPaused(paused: true);
        await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);
    }
}