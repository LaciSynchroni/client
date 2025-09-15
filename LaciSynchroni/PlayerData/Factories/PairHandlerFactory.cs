﻿using LaciSynchroni.FileCache;
using LaciSynchroni.Interop.Ipc;
using LaciSynchroni.PlayerData.Handlers;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.PlayerData.Factories;

public class PairHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SyncMediator _syncMediator;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;

    public PairHandlerFactory(
        ILoggerFactory loggerFactory,
        GameObjectHandlerFactory gameObjectHandlerFactory,
        IpcManager ipcManager,
        FileDownloadManagerFactory fileDownloadManagerFactory,
        DalamudUtilService dalamudUtilService,
        PluginWarningNotificationService pluginWarningNotificationManager,
        IHostApplicationLifetime hostApplicationLifetime,
        FileCacheManager fileCacheManager,
        SyncMediator syncMediator,
        PlayerPerformanceService playerPerformanceService,
        ServerConfigurationManager serverConfigManager
    )
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _dalamudUtilService = dalamudUtilService;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _hostApplicationLifetime = hostApplicationLifetime;
        _fileCacheManager = fileCacheManager;
        _syncMediator = syncMediator;
        _playerPerformanceService = playerPerformanceService;
        _serverConfigManager = serverConfigManager;
    }

    public PairHandler Create(Pair pair)
    {
        return new PairHandler(
            _loggerFactory.CreateLogger<PairHandler>(),
            pair,
            _gameObjectHandlerFactory,
            _ipcManager,
            _fileDownloadManagerFactory.Create(),
            _pluginWarningNotificationManager,
            _dalamudUtilService,
            _hostApplicationLifetime,
            _fileCacheManager,
            _syncMediator,
            _playerPerformanceService,
            _serverConfigManager
        );
    }
}