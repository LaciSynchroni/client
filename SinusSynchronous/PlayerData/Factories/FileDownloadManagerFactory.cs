using Microsoft.Extensions.Logging;
using SinusSynchronous.FileCache;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.WebAPI.Files;

namespace SinusSynchronous.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SinusMediator _sinusMediator;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, SinusMediator sinusMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor)
    {
        _loggerFactory = loggerFactory;
        _sinusMediator = sinusMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _sinusMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor);
    }
}