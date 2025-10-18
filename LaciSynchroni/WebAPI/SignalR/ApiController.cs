using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LaciSynchroni.WebAPI;
using ServerIndex = int;

#pragma warning disable MA0040
public sealed partial class ApiController : DisposableMediatorSubscriberBase
{
    public const string MainServer = "Laci Synchroni";
    public const string MainServiceUri = "wss://sinus.syrilai.dev";

    private readonly DalamudUtilService _dalamudUtil;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILoggerProvider _loggerProvider;
    private readonly MultiConnectTokenService _multiConnectTokenService;
    private readonly SyncConfigService _syncConfigService;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// In preparation for multi-connect, all the SignalR connection functionality
    /// has been moved into an instantiated, not injected, client.
    /// That client, internally, uses the server index to access the server object, and everything is based on that index.
    /// No more usage of ServerConfigurationManager#Current server
    /// Down the line, we move this into a list or a Map of active servers.
    /// </summary>
    private readonly ConcurrentDictionary<ServerIndex, SyncHubClient> _syncClients = new();

    public ApiController(ILogger<ApiController> logger, ILoggerFactory loggerFactory, DalamudUtilService dalamudUtil, ILoggerProvider loggerProvider,
        PairManager pairManager, ServerConfigurationManager serverConfigManager, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) : base(logger, mediator)
    {
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _multiConnectTokenService = multiConnectTokenService;
        _syncConfigService = syncConfigService;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _loggerProvider = loggerProvider;
        
        // When we log out, we could either:
        // - Disconnect all clients
        // - Dispose all clients
        // It seems wise to just discard everything instead of disconnecting them. If we just disconnect them, it might
        // take a tad longer in case of network errors. Potentially, that causes a reconnect during the next login,
        // which might get weird!
        // Better to just throw them away and recreate if needed from scratch!
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DisposeAllClients());
        // We get the login message both when:
        // - the plugin framework updates the first time and the user is logged in
        // - the user manually logged in
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => AutoConnectClients());
    }

    public ServerState GetServerState(ServerIndex index)
    {
        // No client found means it's disconnected. We can't say anything about it's online state
        return GetClientForServer(index)?._serverState ?? ServerState.Disconnected;
    }

    public bool IsServerConnected(int index)
    {
        return GetClientForServer(index)?._serverState == ServerState.Connected;
    }

    public string GetServerNameByIndex(ServerIndex index)
    {
        return _serverConfigManager.GetServerByIndex(index).ServerName ?? string.Empty;
    }

    public int GetOnlineUsersForServer(ServerIndex index)
    {
        return GetClientForServer(index)?.SystemInfoDto?.OnlineUsers ?? 0;
    }

    public bool IsServerAlive(int index)
    {
        var serverState = GetServerState(index);
        return serverState is ServerState.Connected or ServerState.Disconnected;
    }

    public int OnlineUsers
    {
        get
        {
            return _syncClients.Sum(entry => entry.Value.SystemInfoDto?.OnlineUsers ?? 0);
        }
    }

    public ServerInfo? GetServerInfoForServer(ServerIndex index)
    {
        return GetClientForServer(index)?.ConnectionDto?.ServerInfo;
    }

    public DefaultPermissionsDto? GetDefaultPermissionsForServer(ServerIndex index)
    {
        return GetClientForServer(index)?.ConnectionDto?.DefaultPreferredPermissions;
    }

    public bool AnyServerConnected
    {
        get
        {
            return _syncClients.Any(client => client.Value._serverState == ServerState.Connected);
        }
    }

    public bool AnyServerConnecting
    {
        get
        {
            return _syncClients.Any(client => client.Value._serverState == ServerState.Connecting);
        }
    }

    public bool AnyServerDisconnecting
    {
        get
        {
            return _syncClients.Any(client => client.Value._serverState == ServerState.Disconnecting);
        }
    }

    public int[] ConnectedServerIndexes {
        get
        {
            return [.._syncClients.Where(p=> p.Value._serverState == ServerState.Connected)?.Select(p=> p.Key) ?? []];
        }
    }

    public bool IsServerConnecting(ServerIndex index)
    {
        return GetServerState(index) == ServerState.Connecting;
    }

    public int GetMaxGroupsJoinedByUser(ServerIndex serverIndex)
    {
        return GetClientForServer(serverIndex)?.ConnectionDto?.ServerInfo.MaxGroupsJoinedByUser ?? 0;
    }

    public int GetMaxGroupsCreatedByUser(ServerIndex serverIndex)
    {
        return GetClientForServer(serverIndex)?.ConnectionDto?.ServerInfo.MaxGroupsCreatedByUser ?? 0;
    }

    public string? GetAuthFailureMessageByServer(ServerIndex serverIndex)
    {
        return GetClientForServer(serverIndex)?.AuthFailureMessage;
    }

    public string GetUidByServer(ServerIndex serverIndex)
    {
        return GetClientForServer(serverIndex)?.UID ?? string.Empty;
    }

    public string GetDisplayNameByServer(ServerIndex serverIndex)
    {
        return GetClientForServer(serverIndex)?.ConnectionDto?.User.AliasOrUID ?? string.Empty;
    }

    public async Task PauseConnectionAsync(ServerIndex serverIndex)
    {
        _syncClients.TryRemove(serverIndex, out SyncHubClient? removed);
        if (removed != null)
        {
            await removed.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task CreateConnectionsAsync(ServerIndex serverIndex)
    {
        await ConnectMultiClient(serverIndex).ConfigureAwait(false);
    }

    public Task CyclePauseAsync(ServerIndex serverIndex, UserData userData)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            await GetOrCreateForServer(serverIndex).CyclePauseAsync(serverIndex, userData).ConfigureAwait(false);
        }, cts.Token);
        return Task.CompletedTask;
    }

    private SyncHubClient CreateNewClient(ServerIndex serverIndex)
    {
        return new SyncHubClient(serverIndex, _serverConfigManager, _pairManager, _dalamudUtil,
            _loggerFactory, _loggerProvider, Mediator, _multiConnectTokenService, _syncConfigService, _httpClient);
    }

    private SyncHubClient? GetClientForServer(ServerIndex serverIndex)
    {
        _syncClients.TryGetValue(serverIndex, out var client);
        return client;
    }

    private SyncHubClient GetOrCreateForServer(ServerIndex serverIndex)
    {
        return _syncClients.GetOrAdd(serverIndex, CreateNewClient);
    }

    private Task ConnectMultiClient(ServerIndex serverIndex)
    {
        return GetOrCreateForServer(serverIndex).CreateConnectionsAsync();
    }

    public void AutoConnectClients()
    {
        // Fire and forget the auto connect. if something goes wrong, it'll be displayed in UI
        _ = Task.Run(async () =>
        {
            foreach (int serverIndex in _serverConfigManager.ServerIndexes)
            {
                var server = _serverConfigManager.GetServerByIndex(serverIndex);
                // When you manually disconnect a service it gets full paused. In that case, the user explicitly asked for it
                // not to be connected, so we'll just leave it
                // Manually connecting once triggers auto connects again!
                if (!server.FullPause)
                {
                    await GetOrCreateForServer(serverIndex).DalamudUtilOnLogIn().ConfigureAwait(false);
                }
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        DisposeAllClients();
        base.Dispose(disposing);
    }

    private void DisposeAllClients()
    {
        // We can always just Dispose() this - even if the client is currently not connected. Getting rid of them all
        // this way is the safest way to prevent connection leaks. If we'd wait for each of them to disconnect first,
        // we might run into FF14 exiting or similar before they are connected (if you really have to have a lot of connections)
        foreach (var syncHubClient in _syncClients.Values)
        {
            syncHubClient.Dispose();
        }
        _syncClients.Clear();
    }

    public ConnectionDto? GetConnectionDto(ServerIndex serverIndex)
    {
        if (!IsServerConnected(serverIndex))
        {
            return null;
        }
        return _syncClients[serverIndex].ConnectionDto;
    }
}
#pragma warning restore MA0040