using SinusSynchronous.API.Dto.Group;
using SinusSynchronous.WebAPI.SignalR.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace SinusSynchronous.WebAPI;
using ServerIndex = int;

public partial class ApiController
{
    public async Task GroupBanUser(ServerIndex serverIndex, GroupPairDto dto, string reason)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupBanUser(dto, reason).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupBanUser), dto, reason).ConfigureAwait(false);
    }

    public async Task GroupChangeGroupPermissionState(ServerIndex serverIndex, GroupPermissionDto dto)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupChangeGroupPermissionState(dto).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupChangeGroupPermissionState), dto).ConfigureAwait(false);
    }

    public async Task GroupChangeIndividualPermissionState(ServerIndex serverIndex, GroupPairUserPermissionDto dto)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupChangeIndividualPermissionState(dto).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await SetBulkPermissions(serverIndex, new(new(StringComparer.Ordinal),
            new(StringComparer.Ordinal) {
                { dto.Group.GID, dto.GroupPairPermissions }
            })).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupChangeOwnership(groupPair).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupChangeOwnership), groupPair).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(ServerIndex serverIndex, GroupPasswordDto groupPassword)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupChangePassword(groupPassword).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<bool>(nameof(GroupChangePassword), groupPassword).ConfigureAwait(false);
    }

    public async Task GroupClear(ServerIndex serverIndex, GroupDto group)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupClear(group).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupClear), group).ConfigureAwait(false);
    }

    public async Task<GroupJoinDto> GroupCreate()
    {
        if (UseMultiConnect)
        {
            // TODO needs a server selection instead of doing it for current
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.GroupCreate().ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<GroupJoinDto>(nameof(GroupCreate)).ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(ServerIndex serverIndex, GroupDto group, int amount)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupCreateTempInvite(group, amount).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<List<string>>(nameof(GroupCreateTempInvite), group, amount).ConfigureAwait(false);
    }

    public async Task GroupDelete(ServerIndex serverIndex, GroupDto group)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupDelete(group).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupDelete), group).ConfigureAwait(false);
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(ServerIndex serverIndex, GroupDto group)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupGetBannedUsers(group).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<List<BannedGroupUserDto>>(nameof(GroupGetBannedUsers), group).ConfigureAwait(false);
    }

    public Task<GroupJoinInfoDto> GroupJoinCurrentServer(GroupPasswordDto passwordedGroup)
    {
        return GroupJoin(_serverManager.CurrentServerIndex, passwordedGroup);
    }

    public async Task<GroupJoinInfoDto> GroupJoin(ServerIndex serverIndex, GroupPasswordDto passwordedGroup)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupJoin(passwordedGroup).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<GroupJoinInfoDto>(nameof(GroupJoin), passwordedGroup).ConfigureAwait(false);
    }
    
    public Task<bool> GroupJoinFinalizeCurrentServer(GroupJoinDto passwordedGroup)
    {
        return GroupJoinFinalize(_serverManager.CurrentServerIndex, passwordedGroup);
    }

    public async Task<bool> GroupJoinFinalize(ServerIndex serverIndex, GroupJoinDto passwordedGroup)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupJoinFinalize(passwordedGroup).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<bool>(nameof(GroupJoinFinalize), passwordedGroup).ConfigureAwait(false);
    }

    public async Task GroupLeave(ServerIndex serverIndex, GroupDto group)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupLeave(group).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupLeave), group).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupRemoveUser(groupPair).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupRemoveUser), groupPair).ConfigureAwait(false);
    }

    public async Task GroupSetUserInfo(ServerIndex serverIndex, GroupPairUserInfoDto groupPair)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupSetUserInfo(groupPair).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupSetUserInfo), groupPair).ConfigureAwait(false);
    }

    public async Task<int> GroupPrune(ServerIndex serverIndex, GroupDto group, int days, bool execute)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.GroupPrune(group, days, execute).ConfigureAwait(false);
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<int>(nameof(GroupPrune), group, days, execute).ConfigureAwait(false);
    }

    public async Task<List<GroupFullInfoDto>> GroupsGetAll()
    {
        if (UseMultiConnect)
        {
            throw new InvalidOperationException("Not supported for _multiConnect, please call multi connect sinus client instead!");
        }
        CheckConnection();
        return await _sinusHub!.InvokeAsync<List<GroupFullInfoDto>>(nameof(GroupsGetAll)).ConfigureAwait(false);
    }

    public async Task GroupUnbanUser(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.GroupUnbanUser(groupPair).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.SendAsync(nameof(GroupUnbanUser), groupPair).ConfigureAwait(false);
    }

    private void CheckConnection()
    {
        if (ServerState is not (ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting)) throw new InvalidDataException("Not connected");
    }
}