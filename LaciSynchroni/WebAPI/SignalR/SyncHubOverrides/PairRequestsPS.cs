using LaciSynchroni.Common.Data;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services.Mediator;
using MessagePack;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

// PlayerSync's pair request and syncshell invite DTOs. Names and shapes must match PlayerSync's API
// (MareSynchronos.API.Dto.User.UserPairRequestDto and MareSynchronos.API.Dto.Group.GroupJoinInviteDto).

[MessagePackObject(keyAsPropertyName: true)]
public record UserPairRequestDtoPS(string? RequestTargetIdent = null, UserData? UserData = null);

[MessagePackObject(keyAsPropertyName: true)]
public record UserPairRequestFullDtoPS(UserData Requestor, string RequestorIdent, UserData RequestTarget, string RequestTargetIdent);

[MessagePackObject(keyAsPropertyName: true)]
public record UserPairRequestsDtoPS(List<UserPairRequestFullDtoPS> PairingRequests);

[MessagePackObject(keyAsPropertyName: true)]
public record GroupJoinInviteDtoPS(string RequestId, GroupData Group, UserData? InvitingUser);

[MessagePackObject(keyAsPropertyName: true)]
public record GroupJoinInvitesDtoPS(List<GroupJoinInviteDtoPS> GroupJoinInvites);

/// <summary>
/// The pending incoming pair requests and syncshell invites of one PlayerSync server changed.
/// </summary>
public record RequestsChangedMessagePS(int ServerIndex, IReadOnlyList<UserPairRequestFullDtoPS> PairRequests,
    IReadOnlyList<GroupJoinInviteDtoPS> GroupInvites) : MessageBase;

/// <summary>
/// Asks every <see cref="SyncHubClientPS"/> to publish its current lists again, for UI created after they last did.
/// </summary>
public record RequestsRefreshMessagePS : MessageBase;

public enum RequestActionPS
{
    AcceptPairRequest,
    DismissPairRequest,
    AcceptGroupInvite,
    DismissGroupInvite,
}

/// <summary>
/// Sent by the UI to the <see cref="SyncHubClientPS"/> of <paramref name="ServerIndex"/>.
/// <paramref name="Id"/> is the requester's UID for pair requests, and the invite's RequestId for syncshell invites.
/// </summary>
public record RequestActionMessagePS(int ServerIndex, RequestActionPS Action, string Id) : MessageBase;

internal static class PairRequestsExtensionsPS
{
    /// <summary>"Name (alias or UID)" when the user is a visible pair, otherwise only their alias or UID.</summary>
    public static string DescribeUserPS(this PairManager pairManager, int serverIndex, UserData user)
    {
        var name = pairManager.GetPairByUID(serverIndex, user.UID)?.PlayerName;
        return string.IsNullOrEmpty(name) ? user.AliasOrUID : $"{name} ({user.AliasOrUID})";
    }
}
