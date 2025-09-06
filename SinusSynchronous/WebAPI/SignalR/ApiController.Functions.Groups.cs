using SinusSynchronous.API.Dto.Group;

namespace SinusSynchronous.WebAPI;
using ServerIndex = int;

public partial class ApiController
{
    public async Task GroupBanUser(ServerIndex serverIndex, GroupPairDto dto, string reason)
    {
        await GetClientForServer(serverIndex)!.GroupBanUser(dto, reason).ConfigureAwait(false);
    }

    public async Task GroupChangeGroupPermissionState(ServerIndex serverIndex, GroupPermissionDto dto)
    {
        await GetClientForServer(serverIndex)!.GroupChangeGroupPermissionState(dto).ConfigureAwait(false);
    }

    public async Task GroupChangeIndividualPermissionState(ServerIndex serverIndex, GroupPairUserPermissionDto dto)
    {
        await GetClientForServer(serverIndex)!.GroupChangeIndividualPermissionState(dto).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        await GetClientForServer(serverIndex)!.GroupChangeOwnership(groupPair).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(ServerIndex serverIndex, GroupPasswordDto groupPassword)
    {
        return await GetClientForServer(serverIndex)!.GroupChangePassword(groupPassword).ConfigureAwait(false);
    }

    public async Task GroupClear(ServerIndex serverIndex, GroupDto group)
    {
        await GetClientForServer(serverIndex)!.GroupClear(group).ConfigureAwait(false);
    }

    public async Task<GroupJoinDto> GroupCreate(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.GroupCreate().ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(ServerIndex serverIndex, GroupDto group, int amount)
    {
        return await GetClientForServer(serverIndex)!.GroupCreateTempInvite(group, amount).ConfigureAwait(false);
    }

    public async Task GroupDelete(ServerIndex serverIndex, GroupDto group)
    {
        await GetClientForServer(serverIndex)!.GroupDelete(group).ConfigureAwait(false);
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(ServerIndex serverIndex, GroupDto group)
    {
        return await GetClientForServer(serverIndex)!.GroupGetBannedUsers(group).ConfigureAwait(false);
    }

    public Task<GroupJoinInfoDto> GroupJoinForServer(ServerIndex serverIndex, GroupPasswordDto passwordedGroup)
    {
        return GroupJoin(serverIndex, passwordedGroup);
    }

    public async Task<GroupJoinInfoDto> GroupJoin(ServerIndex serverIndex, GroupPasswordDto passwordedGroup)
    {
        return await GetClientForServer(serverIndex)!.GroupJoin(passwordedGroup).ConfigureAwait(false);
    }
    
    public Task<bool> GroupJoinFinalizeForServer(ServerIndex serverIndex, GroupJoinDto passwordedGroup)
    {
        return GroupJoinFinalize(serverIndex, passwordedGroup);
    }

    public async Task<bool> GroupJoinFinalize(ServerIndex serverIndex, GroupJoinDto passwordedGroup)
    {
        return await GetClientForServer(serverIndex)!.GroupJoinFinalize(passwordedGroup).ConfigureAwait(false);
    }

    public async Task GroupLeave(ServerIndex serverIndex, GroupDto group)
    {
        await GetClientForServer(serverIndex)!.GroupLeave(group).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        await GetClientForServer(serverIndex)!.GroupRemoveUser(groupPair).ConfigureAwait(false);
    }

    public async Task GroupSetUserInfo(ServerIndex serverIndex, GroupPairUserInfoDto groupPair)
    {
        await GetClientForServer(serverIndex)!.GroupSetUserInfo(groupPair).ConfigureAwait(false);
    }

    public async Task<int> GroupPrune(ServerIndex serverIndex, GroupDto group, int days, bool execute)
    {
        return await GetClientForServer(serverIndex)!.GroupPrune(group, days, execute).ConfigureAwait(false);
    }


    public async Task GroupUnbanUser(ServerIndex serverIndex, GroupPairDto groupPair)
    {
        await GetClientForServer(serverIndex)!.GroupUnbanUser(groupPair).ConfigureAwait(false);
    }
}