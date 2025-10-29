using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using LaciSynchroni.FileCache;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.UI;
using LaciSynchroni.WebAPI;
using System.Globalization;

namespace LaciSynchroni.Services;

public sealed class CommandManagerService : IDisposable
{
    public const string CommandName = "/laci";
    public const string PluginName = "Laci Synchroni";
    public const string PluginNameShort = "Laci";
    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly SyncMediator _mediator;
    private readonly SyncConfigService _syncConfigService;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public CommandManagerService(ICommandManager commandManager,
        PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner,
        ApiController apiController, SyncMediator mediator, SyncConfigService syncConfigService)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _syncConfigService = syncConfigService;
        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Opens the {PluginName} UI" + Environment.NewLine + Environment.NewLine +
                          "Additionally possible commands:" + Environment.NewLine +
                          $"\t {CommandName} toggle - Disconnects from all {PluginNameShort} servers, if connected. Connects to all {PluginNameShort} servers, if disconnected" +
                          Environment.NewLine +
                          $"\t {CommandName} toggle on|off - Connects or disconnects all {PluginNameShort} servers respectively" +
                          Environment.NewLine +
                          $"\t {CommandName} gpose - Opens the {PluginNameShort} Character Data Hub window" + Environment.NewLine +
                          $"\t {CommandName} analyze - Opens the {PluginNameShort} Character Data Analysis window" + Environment.NewLine +
                          $"\t {CommandName} settings - Opens the {PluginNameShort} Settings window"
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_syncConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (!_syncConfigService.Current.HasValidSetup())
            return;

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            HandleToggleCommand(splitArgs);
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
        else if (string.Equals(splitArgs[0], "servertest", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(LaciServerTesterUi))); 
        }
    }

    private void HandleToggleCommand(string[] splitArgs)
    {
        if (_apiController.AnyServerDisconnecting)
        {
            _mediator.Publish(new NotificationMessage($"A server is disconnecting",
                $"Cannot use {CommandName} toggle while any server is still disconnecting",
                NotificationType.Error));
        }

        if (!_serverConfigurationManager.AnyServerConfigured) return;
        if (splitArgs.Length <= 1)
        {
            TriggerToggleAllServers();
        }
        else if (splitArgs[1].Equals("on"))
        {
            _apiController.AutoConnectClients();
        }
        else if (splitArgs[1].Equals("off"))
        {
            TriggerDisconnectAll();
        }
        else
        {
            TriggerToggleAllServers();
        }
    }

    private void TriggerToggleAllServers()
    {
        _ = ToggleAllServers();
    }

    private void TriggerDisconnectAll()
    {
        _ = DisconnectAllServers();
    }

    private async Task ToggleAllServers()
    {
        foreach (var server in _serverConfigurationManager.ServerInfo.Select(s => s.Id))
        {
            var isConnected = _apiController.IsServerConnected(server);
            if (isConnected)
            {
                await _apiController.PauseConnectionAsync(server).ConfigureAwait(false);
            }
            else
            {
                await _apiController.CreateConnectionsAsync(server).ConfigureAwait(false);
            }
        }
    }

    private async Task DisconnectAllServers()
    {
        foreach (int serverIndex in _serverConfigurationManager.ServerIndexes)
        {
            var isConnected = _apiController.IsServerConnected(serverIndex);
            if (isConnected)
            {
                await _apiController.PauseConnectionAsync(serverIndex).ConfigureAwait(false);
            }
        }
    }
}