using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Data.Enum;
using LaciSynchroni.Common.Data.Extensions;
using LaciSynchroni.Common.Dto.Group;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// PlayerSync's pair requests and syncshell invites: keeps the pending lists of this connection, notifies about new
/// ones, publishes them for the UI (see <see cref="UI.Components.SyncHubOverrides.RequestsSectionPS"/>), and carries out
/// the accept and dismiss actions it sends back.
/// </summary>
internal partial class SyncHubClientPS
{
    private readonly PairManager _pairManagerPS;
    private readonly ServerConfigurationManager _serverConfigurationManagerPS;
    private string ServerNamePS => _serverConfigurationManagerPS.GetServerByIndex(ServerIndex).ServerName;
    private readonly Lock _requestsLock = new();
    private List<UserPairRequestFullDtoPS> _pendingPairRequests = [];
    private List<GroupJoinInviteDtoPS> _pendingGroupInvites = [];

    private void SubscribeToRequestActions()
    {
        Mediator.Subscribe<RequestActionMessagePS>(this, msg =>
        {
            if (msg.ServerIndex == ServerIndex) _ = HandleRequestActionAsync(msg);
        });
        Mediator.Subscribe<RequestsRefreshMessagePS>(this, _ => PublishRequests());
    }

    /// <summary>Called for every new HubConnection. The server sends the full pending lists each time.</summary>
    private void RegisterRequestHandlers()
    {
        lock (_requestsLock)
        {
            _pendingPairRequests = [];
            _pendingGroupInvites = [];
        }
        PublishRequests();

        _connection!.On<UserPairRequestsDtoPS>("Client_UpdatePairRequests", dto =>
        {
            // Only incoming requests, never our own
            var incoming = (dto.PairingRequests ?? []).Where(r => !string.Equals(r.Requestor.UID, UID, StringComparison.Ordinal));
            foreach (var request in ReplacePending(ref _pendingPairRequests, incoming, r => r.Requestor.UID, "pair requests"))
            {
                NotifyNewRequest("New Pair Request", $"{DescribeUser(request.Requestor)} sent you a pair request on {ServerNamePS}.");
            }
        });
        _connection!.On<GroupJoinInvitesDtoPS>("Client_UpdateGroupInvites", dto =>
        {
            foreach (var invite in ReplacePending(ref _pendingGroupInvites, dto.GroupJoinInvites ?? [], i => i.RequestId, "syncshell invites"))
            {
                var from = invite.InvitingUser != null ? DescribeUser(invite.InvitingUser) : "Someone";
                NotifyNewRequest("Syncshell Invite", $"{from} invited you to the syncshell {invite.Group.AliasOrGID} on {ServerNamePS}.");
            }
        });
    }

    /// <summary>Replaces a pending list with the server's latest one, publishes it, and returns the entries that are new.</summary>
    private List<T> ReplacePending<T>(ref List<T> pending, IEnumerable<T> incoming, Func<T, string> key, string what)
    {
        try
        {
            var latest = incoming.DistinctBy(key, StringComparer.Ordinal).ToList();
            List<T> added;
            lock (_requestsLock)
            {
                var known = pending.Select(key).ToHashSet(StringComparer.Ordinal);
                added = latest.Where(e => !known.Contains(key(e))).ToList();
                pending = latest;
            }
            Logger.LogDebug("[PS] Pending {what}: {count} ({new} new)", what, latest.Count, added.Count);
            PublishRequests();
            return added;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[PS] Failed to process {what}", what);
            return [];
        }
    }

    /// <summary>Removes and returns the entry with the given key, or null if it's no longer pending.</summary>
    private T? TakePending<T>(ref List<T> pending, Func<T, string> key, string id) where T : class
    {
        T? taken;
        lock (_requestsLock)
        {
            taken = pending.Find(e => string.Equals(key(e), id, StringComparison.Ordinal));
            if (taken != null) pending = pending.Where(e => !ReferenceEquals(e, taken)).ToList();
        }
        if (taken != null) PublishRequests();
        return taken;
    }

    private void NotifyNewRequest(string title, string message)
        => Mediator.Publish(new NotificationMessage(title, message + " Accept or dismiss it under Pending Requests.",
            NotificationType.Info, TimeSpan.FromSeconds(10)));

    private string DescribeUser(UserData user) => _pairManagerPS.DescribeUserPS(ServerIndex, user);

    private void PublishRequests()
    {
        RequestsChangedMessagePS message;
        lock (_requestsLock)
        {
            message = new(ServerIndex, [.. _pendingPairRequests], [.. _pendingGroupInvites]);
        }
        Mediator.Publish(message);
    }

    private async Task HandleRequestActionAsync(RequestActionMessagePS msg)
    {
        if (!IsConnected) return;

        // Taken out of the list before acting, so a double click doesn't act twice. Put back if the action fails.
        var isPairRequest = msg.Action is RequestActionPS.AcceptPairRequest or RequestActionPS.DismissPairRequest;
        var request = isPairRequest ? TakePending(ref _pendingPairRequests, r => r.Requestor.UID, msg.Id) : null;
        var invite = isPairRequest ? null : TakePending(ref _pendingGroupInvites, i => i.RequestId, msg.Id);
        if (request == null && invite == null) return;

        try
        {
            switch (msg.Action)
            {
                case RequestActionPS.AcceptPairRequest:
                    // PlayerSync pairs two users once both have sent each other a request
                    await _connection!.SendAsync("UserMakePairRequest", new UserPairRequestDtoPS(UserData: new UserData(msg.Id))).ConfigureAwait(false);
                    break;

                case RequestActionPS.DismissPairRequest:
                    // Silently drops the request, the requester isn't told
                    await _connection!.SendAsync("UserRejectPairRequest", new UserPairRequestDtoPS(UserData: new UserData(msg.Id))).ConfigureAwait(false);
                    break;

                case RequestActionPS.AcceptGroupInvite:
                    await AcceptGroupInviteAsync(invite!).ConfigureAwait(false);
                    break;

                case RequestActionPS.DismissGroupInvite:
                    // PlayerSync expects our own UID as the inviting user here
                    await _connection!.SendAsync("GroupUserRejectInvite", invite! with { InvitingUser = new UserData(UID) }).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[PS] Failed to {action} for {id}", msg.Action, msg.Id);
            Mediator.Publish(new NotificationMessage("Request failed", $"Could not complete the request on {ServerNamePS}. See /xllog for details.",
                NotificationType.Error));
            // Unless a list the server sent in the meantime already brought it back
            lock (_requestsLock)
            {
                if (request != null && !_pendingPairRequests.Exists(r => string.Equals(r.Requestor.UID, msg.Id, StringComparison.Ordinal)))
                    _pendingPairRequests = [.. _pendingPairRequests, request];
                if (invite != null && !_pendingGroupInvites.Exists(i => string.Equals(i.RequestId, msg.Id, StringComparison.Ordinal)))
                    _pendingGroupInvites = [.. _pendingGroupInvites, invite];
            }
            PublishRequests();
            return;
        }

        // Done: drop the entry again in case a list the server sent in the meantime brought it back
        if (isPairRequest) TakePending(ref _pendingPairRequests, r => r.Requestor.UID, msg.Id);
        else TakePending(ref _pendingGroupInvites, i => i.RequestId, msg.Id);
    }

    private async Task AcceptGroupInviteAsync(GroupJoinInviteDtoPS invite)
    {
        // The invite's RequestId works as the syncshell password
        var joinInfo = await GroupJoin(new GroupPasswordDto(invite.Group, invite.RequestId)).ConfigureAwait(false);
        if (!joinInfo.Success)
        {
            Mediator.Publish(new NotificationMessage("Syncshell Invite",
                $"Could not join {invite.Group.AliasOrGID}: the invite may have expired, the syncshell may be full, or you may already be in it.",
                NotificationType.Warning));
            return;
        }

        // Join with our default syncshell permissions, same as finalizing in the Join Syncshell window without changes
        var defaults = ConnectionDto?.DefaultPreferredPermissions;
        var joinPermissions = GroupUserPreferredPermissions.NoneSet;
        joinPermissions.SetDisableSounds(defaults?.DisableGroupSounds ?? false);
        joinPermissions.SetDisableAnimations(defaults?.DisableGroupAnimations ?? false);
        joinPermissions.SetDisableVFX(defaults?.DisableGroupVFX ?? false);
        var joined = await GroupJoinFinalize(new GroupJoinDto(joinInfo.Group, invite.RequestId, joinPermissions)).ConfigureAwait(false);

        Mediator.Publish(new NotificationMessage("Syncshell Invite",
            joined ? $"Joined syncshell {joinInfo.GroupAliasOrGID}." : $"Could not join syncshell {joinInfo.GroupAliasOrGID}.",
            joined ? NotificationType.Info : NotificationType.Warning));
    }
}
