using SinusSynchronous.API.Data;
using SinusSynchronous.Services;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.Utils;
using SinusSynchronous.WebAPI;
using SinusSynchronous.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace SinusSynchronous.PlayerData.Pairs;

public class VisibleUserDataDistributor : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private CharacterData? _lastCreatedData;
    private CharacterData? _uploadingCharacterData = null;
    private readonly List<ServerBasedUserKey> _previouslyVisiblePlayers = [];
    private Task<CharacterData>? _fileUploadTask = null;
    private readonly HashSet<ServerBasedUserKey> _usersToPushDataTo = [];
    private readonly SemaphoreSlim _pushDataSemaphore = new(1, 1);
    private readonly CancellationTokenSource _runtimeCts = new();


    public VisibleUserDataDistributor(ILogger<VisibleUserDataDistributor> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, SinusMediator mediator, FileUploadManager fileTransferManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = msg.CharacterData;
            if (_lastCreatedData == null || (!string.Equals(newData.DataHash.Value, _lastCreatedData.DataHash.Value, StringComparison.Ordinal)))
            {
                _lastCreatedData = newData;
                Logger.LogTrace("Storing new data hash {hash}", newData.DataHash.Value);
                PushToAllVisibleUsers(forced: true);
            }
            else
            {
                Logger.LogTrace("Data hash {hash} equal to stored data", newData.DataHash.Value);
            }
        });

        Mediator.Subscribe<ConnectedMessage>(this, (msg) => PushToAllVisibleUsers(false, msg.serverIndex));
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _previouslyVisiblePlayers.Clear());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtimeCts.Cancel();
            _runtimeCts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PushToAllVisibleUsers(bool forced = false, int? serverIndex = null)
    {
        if (serverIndex != null)
        {
            foreach (var user in _pairManager.GetVisibleUsers((int)serverIndex))
            {
                _usersToPushDataTo.Add(user);
            }
        }
        else
        {
            foreach (var user in _pairManager.GetVisibleUsersAcrossAllServers())
            {
                _usersToPushDataTo.Add(user);
            }
        }

        if (_usersToPushDataTo.Count > 0)
        {
            Logger.LogDebug("Pushing data {hash} for {count} visible players", _lastCreatedData?.DataHash.Value ?? "UNKNOWN", _usersToPushDataTo.Count);
            foreach (int connectedServerIndex in _apiController.ConnectedServerIndexes)
            {
                PushCharacterData(connectedServerIndex, forced);
            }
        }
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected) return;

        var allVisibleUsers = _pairManager.GetVisibleUsersAcrossAllServers();
        var newVisibleUsers = allVisibleUsers.Except(_previouslyVisiblePlayers).ToList();
        _previouslyVisiblePlayers.Clear();
        _previouslyVisiblePlayers.AddRange(allVisibleUsers);
        if (newVisibleUsers.Count == 0) return;

        Logger.LogDebug("Scheduling character data push of {data} to {users}",
            _lastCreatedData?.DataHash.Value ?? string.Empty,
            string.Join(", ", newVisibleUsers.Select(k => k.UserData.AliasOrUID)));
        foreach (var user in newVisibleUsers)
        {
            _usersToPushDataTo.Add(user);
        }
        foreach (int connectedServerIndex in _apiController.ConnectedServerIndexes)
        {
            PushCharacterData(connectedServerIndex);
        }
    }

    private void PushCharacterData(int serverIndex, bool forced = false)
    {
        if (_lastCreatedData == null || _usersToPushDataTo.Count == 0) return;

        _ = Task.Run(async () =>
        {
            forced |= _uploadingCharacterData?.DataHash != _lastCreatedData.DataHash;

            if (_fileUploadTask == null || (_fileUploadTask?.IsCompleted ?? false) || forced)
            {
                _uploadingCharacterData = _lastCreatedData.DeepClone();
                Logger.LogDebug("Starting UploadTask for {hash}, Reason: TaskIsNull: {task}, TaskIsCompleted: {taskCpl}, Forced: {frc}",
                    _lastCreatedData.DataHash, _fileUploadTask == null, _fileUploadTask?.IsCompleted ?? false, forced);
                // Right now, the file manager is still single-instance, so we push to "current" independant of the user
                var usersToPushDataTo = _usersToPushDataTo.Select(key => key.UserData);
                _fileUploadTask = _fileTransferManager.UploadFiles(serverIndex, _uploadingCharacterData, [.. usersToPushDataTo]);
            }

            if (_fileUploadTask != null)
            {
                var dataToSend = await _fileUploadTask.ConfigureAwait(false);

                try
                {
                    await _pushDataSemaphore.WaitAsync(_runtimeCts.Token).ConfigureAwait(false);
                    if (_usersToPushDataTo.Count == 0) return;

                    var serversToPushTo = _usersToPushDataTo.Select(key => key.ServerIndex).Distinct();
                    Logger.LogDebug("Pushing to servers: {serversToPushTo}", serversToPushTo);
                    foreach (int serverIndex in serversToPushTo)
                    {
                        Logger.LogDebug("Server {serverIndex}: Pushing {data} to {users}", serverIndex,
                            dataToSend.DataHash,
                            string.Join(", ", _usersToPushDataTo.Select(k => k.UserData.AliasOrUID)));
                        var toPushForServer = _usersToPushDataTo.Where(key => key.ServerIndex == serverIndex)
                            .Select(key => key.UserData);
                        await _apiController.PushCharacterData(serverIndex, dataToSend, [.. toPushForServer])
                            .ConfigureAwait(false);
                    }

                    _usersToPushDataTo.Clear();
                }
                catch (Exception e)
                {
                    Logger.LogDebug(e, "Failed to acquire lock.");
                }
                finally
                {
                    _pushDataSemaphore.Release();
                }
            }
        });
    }
}