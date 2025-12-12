using LaciSynchroni.FileCache;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Configurations;
using LaciSynchroni.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SyncMediator _syncMediator;
    private readonly ServerConfigurationManager _serverManager;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, SyncMediator syncMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, ServerConfigurationManager serverManager)
    {
        _loggerFactory = loggerFactory;
        _syncMediator = syncMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _serverManager = serverManager;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _syncMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor, _serverManager);
    }
}