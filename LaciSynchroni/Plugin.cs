﻿using System.Net.Http.Headers;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LaciSynchroni.FileCache;
using LaciSynchroni.Interop;
using LaciSynchroni.Interop.Ipc;
using LaciSynchroni.PlayerData.Factories;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.PlayerData.Services;
using LaciSynchroni.Services;
using LaciSynchroni.Services.CharaData;
using LaciSynchroni.Services.Events;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Configurations;
using LaciSynchroni.UI;
using LaciSynchroni.UI.Components;
using LaciSynchroni.UI.Components.Popup;
using LaciSynchroni.UI.Handlers;
using LaciSynchroni.WebAPI;
using LaciSynchroni.WebAPI.Files;
using LaciSynchroni.WebAPI.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;

namespace LaciSynchroni;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target) {
        foreach (var dir in source.GetDirectories())
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name));
    }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager gameData,
        IFramework framework,
        IObjectTable objectTable,
        IClientState clientState,
        ICondition condition,
        IChatGui chatGui,
        IGameGui gameGui,
        IDtrBar dtrBar,
        IPluginLog pluginLog,
        ITargetManager targetManager,
        INotificationManager notificationManager,
        ITextureProvider textureProvider,
        IContextMenu contextMenu,
        IGameInteropProvider gameInteropProvider,
        IGameConfig gameConfig,
        ISigScanner sigScanner
    )
    {
        var configDir = Path.GetFullPath(Path.Combine(pluginInterface.ConfigFile.FullName, "..", pluginInterface.InternalName));
        var legacyConfigDir = Path.GetFullPath(Path.Combine(configDir, "..", "SinusSynchronous"));
        if (!Directory.Exists(configDir) && Directory.Exists(legacyConfigDir))
        {
            pluginLog.Info($"Found old config at {legacyConfigDir}, copying to {configDir}");
            CopyFilesRecursively(new DirectoryInfo(legacyConfigDir), new DirectoryInfo(configDir));
        }

        var traceDir = Path.Join(pluginInterface.ConfigDirectory.FullName, "tracelog");
        if (!Directory.Exists(traceDir))
            Directory.CreateDirectory(traceDir);

        foreach (
            var file in Directory
                .EnumerateFiles(traceDir)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(9)
        )
        {
            int attempts = 0;
            bool deleted = false;
            while (!deleted && attempts < 5)
            {
                try
                {
                    file.Delete();
                    deleted = true;
                }
                catch
                {
                    attempts++;
                    Thread.Sleep(500);
                }
            }
        }

        _host = new HostBuilder()
            .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
            .ConfigureLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddDalamudLogging(pluginLog, gameData.HasModifiedGameDataFiles);
                lb.AddFile(
                    Path.Combine(traceDir, $"laci-trace-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log"),
                    (opt) =>
                    {
                        opt.Append = true;
                        opt.RollingFilesConvention = FileLoggerOptions
                            .FileRollingConvention
                            .Ascending;
                        opt.MinLevel = LogLevel.Trace;
                        opt.FileSizeLimitBytes = 50 * 1024 * 1024;
                    }
                );
                lb.SetMinimumLevel(LogLevel.Trace);
            })
            .ConfigureServices(collection =>
            {
                collection.AddSingleton(new WindowSystem("LaciSynchroni"));
                collection.AddSingleton<FileDialogManager>();
                collection.AddSingleton(
                    new Dalamud.Localization(
                        "LaciSynchroni.Localization.",
                        "",
                        useEmbedded: true
                    )
                );

                collection.AddSingleton<SyncMediator>();
                collection.AddSingleton<FileCacheManager>();
                collection.AddSingleton<ServerConfigurationManager>();
                collection.AddSingleton<ApiController>();
                collection.AddSingleton<PerformanceCollectorService>();
                collection.AddSingleton<HubFactory>();
                collection.AddSingleton<FileUploadManager>();
                collection.AddSingleton<FileTransferOrchestrator>();
                collection.AddSingleton<LaciPlugin>();
                collection.AddSingleton<ProfileManager>();
                collection.AddSingleton<GameObjectHandlerFactory>();
                collection.AddSingleton<FileDownloadManagerFactory>();
                collection.AddSingleton<PairHandlerFactory>();
                collection.AddSingleton<PairFactory>();
                collection.AddSingleton<XivDataAnalyzer>();
                collection.AddSingleton<CharacterAnalyzer>();
                collection.AddSingleton<TokenProvider>();
                collection.AddSingleton<PluginWarningNotificationService>();
                collection.AddSingleton<FileCompactor>();
                collection.AddSingleton<TagHandler>();
                collection.AddSingleton<IdDisplayHandler>();
                collection.AddSingleton<PlayerPerformanceService>();
                collection.AddSingleton<TransientResourceManager>();

                collection.AddSingleton<CharaDataManager>();
                collection.AddSingleton<CharaDataFileHandler>();
                collection.AddSingleton<CharaDataCharacterHandler>();
                collection.AddSingleton<CharaDataNearbyManager>();
                collection.AddSingleton<CharaDataGposeTogetherManager>();

                collection.AddSingleton(s => new VfxSpawnManager(
                    s.GetRequiredService<ILogger<VfxSpawnManager>>(),
                    gameInteropProvider,
                    s.GetRequiredService<SyncMediator>()
                ));
                collection.AddSingleton(
                    (s) =>
                        new BlockedCharacterHandler(
                            s.GetRequiredService<ILogger<BlockedCharacterHandler>>(),
                            gameInteropProvider
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcProvider(
                            s.GetRequiredService<ILogger<IpcProvider>>(),
                            pluginInterface,
                            s.GetRequiredService<CharaDataManager>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton<SelectPairForTagUi>();
                collection.AddSingleton(
                    (s) =>
                        new EventAggregator(
                            pluginInterface.ConfigDirectory.FullName,
                            s.GetRequiredService<ILogger<EventAggregator>>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new DalamudUtilService(
                            s.GetRequiredService<ILogger<DalamudUtilService>>(),
                            clientState,
                            objectTable,
                            framework,
                            gameGui,
                            condition,
                            gameData,
                            targetManager,
                            gameConfig,
                            s.GetRequiredService<BlockedCharacterHandler>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<PerformanceCollectorService>(),
                            s.GetRequiredService<SyncConfigService>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new DtrEntry(
                            s.GetRequiredService<ILogger<DtrEntry>>(),
                            dtrBar,
                            s.GetRequiredService<SyncConfigService>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<PairManager>(),
                            s.GetRequiredService<ApiController>()
                        )
                );
                collection.AddSingleton(s => new PairManager(
                    s.GetRequiredService<ILogger<PairManager>>(),
                    s.GetRequiredService<PairFactory>(),
                    s.GetRequiredService<SyncConfigService>(),
                    s.GetRequiredService<SyncMediator>(),
                    contextMenu
                ));
                collection.AddSingleton<RedrawManager>();
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerPenumbra(
                            s.GetRequiredService<ILogger<IpcCallerPenumbra>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<RedrawManager>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerGlamourer(
                            s.GetRequiredService<ILogger<IpcCallerGlamourer>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<RedrawManager>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerCustomize(
                            s.GetRequiredService<ILogger<IpcCallerCustomize>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerHeels(
                            s.GetRequiredService<ILogger<IpcCallerHeels>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerHonorific(
                            s.GetRequiredService<ILogger<IpcCallerHonorific>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerMoodles(
                            s.GetRequiredService<ILogger<IpcCallerMoodles>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerPetNames(
                            s.GetRequiredService<ILogger<IpcCallerPetNames>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcCallerBrio(
                            s.GetRequiredService<ILogger<IpcCallerBrio>>(),
                            pluginInterface,
                            s.GetRequiredService<DalamudUtilService>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new IpcManager(
                            s.GetRequiredService<ILogger<IpcManager>>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<IpcCallerPenumbra>(),
                            s.GetRequiredService<IpcCallerGlamourer>(),
                            s.GetRequiredService<IpcCallerCustomize>(),
                            s.GetRequiredService<IpcCallerHeels>(),
                            s.GetRequiredService<IpcCallerHonorific>(),
                            s.GetRequiredService<IpcCallerMoodles>(),
                            s.GetRequiredService<IpcCallerPetNames>(),
                            s.GetRequiredService<IpcCallerBrio>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                        new NotificationService(
                            s.GetRequiredService<ILogger<NotificationService>>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<DalamudUtilService>(),
                            notificationManager,
                            chatGui,
                            s.GetRequiredService<SyncConfigService>()
                        )
                );
                collection.AddSingleton(
                    (s) =>
                    {
                        var httpClient = new HttpClient();
                        var ver = Assembly.GetExecutingAssembly().GetName().Version;
                        httpClient.DefaultRequestHeaders.UserAgent.Add(
                            new ProductInfoHeaderValue(
                                "LaciSynchroni",
                                ver!.Major + "." + ver!.Minor + "." + ver!.Build
                            )
                        );
                        return httpClient;
                    }
                );
                collection.AddSingleton(
                    (s) => new SyncConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) =>
                        new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton(
                    (s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName)
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<SyncConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<ServerConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<NotesConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<ServerTagConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<TransientConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<XivDataStorageService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<PlayerPerformanceConfigService>()
                );
                collection.AddSingleton<IConfigService<ISyncConfiguration>>(s =>
                    s.GetRequiredService<CharaDataConfigService>()
                );
                collection.AddSingleton<ConfigurationMigrator>();
                collection.AddSingleton<ConfigurationSaveService>();

                collection.AddSingleton<HubFactory>();

                // add scoped services
                collection.AddScoped<DrawEntityFactory>();
                collection.AddScoped<CacheMonitor>();
                collection.AddScoped<UiFactory>();
                collection.AddScoped<SelectTagForPairUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
                collection.AddScoped<WindowMediatorSubscriberBase, JoinSyncshellUI>();
                collection.AddScoped<WindowMediatorSubscriberBase, CreateSyncshellUI>();
                collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
                collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();

                collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>(
                    (s) =>
                        new EditProfileUi(
                            s.GetRequiredService<ILogger<EditProfileUi>>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<ApiController>(),
                            s.GetRequiredService<UiSharedService>(),
                            s.GetRequiredService<FileDialogManager>(),
                            s.GetRequiredService<ProfileManager>(),
                            s.GetRequiredService<PerformanceCollectorService>()
                        )
                );
                collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
                collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
                collection.AddScoped<IPopupHandler, CensusPopupHandler>();
                collection.AddScoped<CacheCreationService>();
                collection.AddScoped<PlayerDataFactory>();
                collection.AddScoped<VisibleUserDataDistributor>();
                collection.AddScoped(
                    (s) =>
                        new UiService(
                            s.GetRequiredService<ILogger<UiService>>(),
                            pluginInterface.UiBuilder,
                            s.GetRequiredService<SyncConfigService>(),
                            s.GetRequiredService<WindowSystem>(),
                            s.GetServices<WindowMediatorSubscriberBase>(),
                            s.GetRequiredService<UiFactory>(),
                            s.GetRequiredService<FileDialogManager>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );
                collection.AddScoped(
                    (s) =>
                        new CommandManagerService(
                            commandManager,
                            s.GetRequiredService<PerformanceCollectorService>(),
                            s.GetRequiredService<ServerConfigurationManager>(),
                            s.GetRequiredService<CacheMonitor>(),
                            s.GetRequiredService<ApiController>(),
                            s.GetRequiredService<SyncMediator>(),
                            s.GetRequiredService<SyncConfigService>()
                        )
                );
                collection.AddScoped(
                    (s) =>
                        new UiSharedService(
                            s.GetRequiredService<ILogger<UiSharedService>>(),
                            s.GetRequiredService<IpcManager>(),
                            s.GetRequiredService<ApiController>(),
                            s.GetRequiredService<CacheMonitor>(),
                            s.GetRequiredService<FileDialogManager>(),
                            s.GetRequiredService<SyncConfigService>(),
                            s.GetRequiredService<DalamudUtilService>(),
                            pluginInterface,
                            textureProvider,
                            s.GetRequiredService<Dalamud.Localization>(),
                            s.GetRequiredService<ServerConfigurationManager>(),
                            s.GetRequiredService<TokenProvider>(),
                            s.GetRequiredService<SyncMediator>()
                        )
                );

                collection.AddHostedService(p => p.GetRequiredService<ConfigurationSaveService>());
                collection.AddHostedService(p => p.GetRequiredService<SyncMediator>());
                collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
                collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
                collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
                collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
                collection.AddHostedService(p =>
                    p.GetRequiredService<PerformanceCollectorService>()
                );
                collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
                collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
                collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
                collection.AddHostedService(p => p.GetRequiredService<LaciPlugin>());
            })
            .Build();

        _ = _host.StartAsync();
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}