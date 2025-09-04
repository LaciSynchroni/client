using Dalamud.Plugin.Services;
using SinusSynchronous.API.Data;
using SinusSynchronous.API.Data.Comparer;
using SinusSynchronous.API.Data.Extensions;
using SinusSynchronous.API.Dto.Group;
using SinusSynchronous.API.Dto.User;
using SinusSynchronous.SinusConfiguration;
using SinusSynchronous.SinusConfiguration.Models;
using SinusSynchronous.PlayerData.Factories;
using SinusSynchronous.Services.Events;
using SinusSynchronous.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace SinusSynchronous.PlayerData.Pairs;

public sealed class PairManager : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<ServerBasedUserKey, Pair> _allClientPairs =
        new(ServerBasedUserKeyComparator.Instance);

    private readonly ConcurrentDictionary<ServerBasedGroupKey, GroupFullInfoDto> _allGroups =
        new(ServerBasedGroupKeyComparator.Instance);

    private readonly SinusConfigService _configurationService;
    private readonly IContextMenu _dalamudContextMenu;
    private readonly PairFactory _pairFactory;
    private Lazy<List<Pair>> _directPairsInternal;
    private Lazy<Dictionary<GroupFullInfoWithServer, List<Pair>>> _groupPairsInternal;
    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> _pairsWithGroupsInternal;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory,
        SinusConfigService configurationService, SinusMediator mediator,
        IContextMenu dalamudContextMenu) : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        _dalamudContextMenu = dalamudContextMenu;
        Mediator.Subscribe<DisconnectedMessage>(this, (msg) => ClearPairs(msg.ServerIndex));
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => ReapplyPairData());
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();

        _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
    }

    public List<Pair> DirectPairs => _directPairsInternal.Value;

    public Dictionary<GroupFullInfoWithServer, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public Dictionary<ServerBasedGroupKey, GroupFullInfoDto> Groups => _allGroups.ToDictionary(k => k.Key, k => k.Value);
    public Pair? LastAddedUser { get; internal set; }
    public Dictionary<Pair, List<GroupFullInfoDto>> PairsWithGroups => _pairsWithGroupsInternal.Value;

    public void AddGroup(GroupFullInfoDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key] = dto;
        RecreateLazy();
    }

    public void AddGroupPair(GroupPairFullInfoDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.ContainsKey(key))
            _allClientPairs[key] = _pairFactory.Create(new UserFullPairDto(dto.User,
                API.Data.Enum.IndividualPairStatus.None,
                [dto.Group.GID], dto.SelfToOtherPermissions, dto.OtherToSelfPermissions), serverIndex);
        else _allClientPairs[key].UserPair.Groups.Add(dto.GID);
        RecreateLazy();
    }

    public Pair? GetPairByUID(string uid)
    {
        // TODO needs a server?
        var existingPair =
            _allClientPairs.FirstOrDefault(f => string.Equals(f.Key.UserData.UID, uid, StringComparison.Ordinal));
        if (!Equals(existingPair, default(KeyValuePair<ServerBasedUserKey, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    public void AddUserPair(UserFullPairDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.ContainsKey(key))
        {
            _allClientPairs[key] = _pairFactory.Create(dto, serverIndex);
        }
        else
        {
            _allClientPairs[key].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            _allClientPairs[key].ApplyLastReceivedData();
        }

        RecreateLazy();
    }

    public void AddUserPair(UserPairDto dto, int serverIndex, bool addToLastAddedUser = true)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.ContainsKey(key))
        {
            _allClientPairs[key] = _pairFactory.Create(dto, serverIndex);
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[key].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
        _allClientPairs[key].UserPair.OwnPermissions = dto.OwnPermissions;
        _allClientPairs[key].UserPair.OtherPermissions = dto.OtherPermissions;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[key];
        _allClientPairs[key].ApplyLastReceivedData();
        RecreateLazy();
    }

    public void ClearPairs(int serverIndex)
    {
        Logger.LogDebug("Clearing all Pairs");
        DisposePairs(serverIndex);
        _allClientPairs.Keys
            .Where(key => key.ServerIndex == serverIndex)
            .ToList()
            .ForEach(key => _allClientPairs.Remove(key, out _));
        _allGroups.Keys
            .Where(key => key.ServerIndex == serverIndex)
            .ToList()
            .ForEach(key => _allGroups.Remove(key, out _));
        RecreateLazy();
    }

    public List<Pair> GetOnlineUserPairs(int serverIndex) => _allClientPairs
        .Where(p => p.Key.ServerIndex == serverIndex)
        .Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();

    public List<Pair> GetOnlineUserPairsAcrossAllServers() => _allClientPairs
        .Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();
    
    public int GetVisibleUserCountAcrossAllServers() => _allClientPairs
        .Count(p => p.Value.IsVisible);

    public List<ServerBasedUserKey> GetVisibleUsers(int serverIndex) =>
    [
        .. _allClientPairs
            .Where(p => p.Key.ServerIndex == serverIndex)
            .Where(p => p.Value.IsVisible)
            .Select(p => p.Key)
    ];
    
    public List<ServerBasedUserKey> GetVisibleUsersAcrossAllServers() =>
    [
        .. _allClientPairs
            .Where(p => p.Value.IsVisible)
            .Select(p => p.Key)
    ];

    public void MarkPairOffline(UserData user, int serverIndex)
    {
        var key = BuildKey(user, serverIndex);
        if (_allClientPairs.TryGetValue(key, out var pair))
        {
            var message = new ServerBasedUserKey(user, serverIndex);
            Mediator.Publish(new ClearProfileDataMessage(message));
            pair.MarkOffline();
        }

        RecreateLazy();
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, int serverIndex, bool sendNotif = true)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.ContainsKey(key)) throw new InvalidOperationException("No user found for " + dto);

        var message = new ServerBasedUserKey(dto.User, serverIndex);
        Mediator.Publish(new ClearProfileDataMessage(message));

        var pair = _allClientPairs[key];
        if (pair.HasCachedPlayer)
        {
            RecreateLazy();
            return;
        }

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
                      && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs &&
                          pair.IsDirectlyPaired && !pair.IsOneSidedPair
                          || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
                      && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs &&
                          !string.IsNullOrEmpty(pair.GetNote())
                          || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNote();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(
                new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5)));
        }

        pair.CreateCachedPlayer(dto);

        RecreateLazy();
    }

    public void ReceiveCharaData(OnlineUserCharaDataDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.TryGetValue(key, out var pair))
            throw new InvalidOperationException("No user found for " + dto.User);

        // TODO index yes yes
        Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational,
            "Received Character Data")));
        _allClientPairs[key].ApplyData(dto);
    }

    public void RemoveGroup(GroupData data, int serverIndex)
    {
        var key = BuildKey(data, serverIndex);
        _allGroups.TryRemove(key, out _);

        foreach (var item in _allClientPairs.ToList())
        {
            item.Value.UserPair.Groups.Remove(data.GID);

            if (!item.Value.HasAnyConnection())
            {
                item.Value.MarkOffline();
                _allClientPairs.TryRemove(item.Key, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveGroupPair(GroupPairDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (_allClientPairs.TryGetValue(key, out var pair))
        {
            pair.UserPair.Groups.Remove(dto.Group.GID);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(key, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveUserPair(UserDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (_allClientPairs.TryGetValue(key, out var pair))
        {
            pair.UserPair.IndividualPairStatus = API.Data.Enum.IndividualPairStatus.None;

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(key, out _);
            }
        }

        RecreateLazy();
    }

    public void SetGroupInfo(GroupInfoDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key].Group = dto.Group;
        _allGroups[key].Owner = dto.Owner;
        _allGroups[key].GroupPermissions = dto.GroupPermissions;

        RecreateLazy();
    }

    public void UpdatePairPermissions(UserPermissionsDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.TryGetValue(key, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null)
        {
            throw new InvalidOperationException("No direct pair for " + dto);
        }

        if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            var message = new ServerBasedUserKey(dto.User, serverIndex);
            Mediator.Publish(new ClearProfileDataMessage(message));
        }

        pair.UserPair.OtherPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OtherPermissions.IsPaused(),
            pair.UserPair.OtherPermissions.IsDisableAnimations(),
            pair.UserPair.OtherPermissions.IsDisableSounds(),
            pair.UserPair.OtherPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (!_allClientPairs.TryGetValue(key, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            var message = new ServerBasedUserKey(dto.User, serverIndex);
            Mediator.Publish(new ClearProfileDataMessage(message));
        }

        pair.UserPair.OwnPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OwnPermissions.IsPaused(),
            pair.UserPair.OwnPermissions.IsDisableAnimations(),
            pair.UserPair.OwnPermissions.IsDisableSounds(),
            pair.UserPair.OwnPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    internal void ReceiveUploadStatus(UserDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (_allClientPairs.TryGetValue(key, out var existingPair) && existingPair.IsVisible)
        {
            existingPair.SetIsUploading();
        }
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key].GroupPairUserInfos[dto.UID] = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key].GroupPermissions = dto.Permissions;
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key].GroupUserInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void UpdateGroupPairPermissions(GroupPairUserPermissionDto dto, int serverIndex)
    {
        var key = BuildKey(dto.Group, serverIndex);
        _allGroups[key].GroupUserPermissions = dto.GroupPairPermissions;
        RecreateLazy();
    }

    internal void UpdateIndividualPairStatus(UserIndividualPairStatusDto dto, int serverIndex)
    {
        var key = BuildKey(dto.User, serverIndex);
        if (_allClientPairs.TryGetValue(key, out var pair))
        {
            pair.UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            RecreateLazy();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

        DisposePairs(null);
    }

    private void DalamudContextMenuOnOnOpenGameObjectContextMenu(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        if (args.MenuType == Dalamud.Game.Gui.ContextMenu.ContextMenuType.Inventory) return;
        if (!_configurationService.Current.EnableRightClickMenus) return;

        foreach (var pair in _allClientPairs.Where((p => p.Value.IsVisible)))
        {
            pair.Value.AddContextMenu(args);
        }
    }

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value)
        .Where(k => k.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None).ToList());

    private void DisposePairs(int? serverIndex)
    {
        if (serverIndex == null)
        {
            Logger.LogDebug("Disposing all Pairs");
            Parallel.ForEach(_allClientPairs, item =>
            {
                item.Value.MarkOffline(wait: false);
            });
        }
        else
        {
            Logger.LogDebug("Disposing all Pairs for server {serverIndex}", serverIndex);
            var toDispose = _allClientPairs.Where(index => index.Key.ServerIndex == serverIndex);
            Parallel.ForEach(toDispose, item =>
            {
                item.Value.MarkOffline(wait: false);
            });
        }
      
    }

    private Lazy<Dictionary<GroupFullInfoWithServer, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoWithServer, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoWithServer, List<Pair>> outDict = [];
            foreach (var group in _allGroups)
            {
                var key = new GroupFullInfoWithServer(group.Key.ServerIndex, group.Value);
                outDict[key] = _allClientPairs.Select(p => p.Value).Where(p =>
                    p.UserPair.Groups.Exists(g => GroupDataComparer.Instance.Equals(group.Key.GroupData, new(g)))).ToList();
            }

            return outDict;
        });
    }

    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> PairsWithGroupsLazy()
    {
        return new Lazy<Dictionary<Pair, List<GroupFullInfoDto>>>(() =>
        {
            Dictionary<Pair, List<GroupFullInfoDto>> outDict = [];

            foreach (var pair in _allClientPairs.Select(k => k.Value))
            {
                outDict[pair] = _allGroups.Where(k => pair.UserPair.Groups.Contains(k.Key.GroupData.GID, StringComparer.Ordinal))
                    .Select(k => k.Value).ToList();
            }

            return outDict;
        });
    }

    private void ReapplyPairData()
    {
        foreach (var pair in _allClientPairs.Select(k => k.Value))
        {
            pair.ApplyLastReceivedData(forced: true);
        }
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();
        Mediator.Publish(new RefreshUiMessage());
    }

    private ServerBasedUserKey BuildKey(UserData user, int serverIndex)
    {
        return new ServerBasedUserKey(user, serverIndex);
    }

    private ServerBasedGroupKey BuildKey(GroupData group, int serverIndex)
    {
        return new ServerBasedGroupKey(group, serverIndex);
    }
}