using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using LaciSynchroni.Common.Data;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LaciSynchroni.UI.Components.SyncHubOverrides;

/// <summary>
/// PlayerSync's incoming pair requests and syncshell invites, drawn inside the "Pending Requests" folder
/// through <see cref="IPendingRequestsProvider"/>.
/// The PlayerSync hub client (<see cref="SyncHubClientPS"/>) keeps the pending lists and does the accepting and
/// dismissing; this only shows the latest snapshot per server and sends the button clicks back through the mediator.
/// Stays empty, and draws nothing, for anyone without a PlayerSync server.
/// </summary>
internal sealed class RequestsSectionPS : DisposableMediatorSubscriberBase, IPendingRequestsProvider
{
    private readonly UiSharedService _uiSharedService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PairManager _pairManager;
    private readonly ConcurrentDictionary<int, RequestsChangedMessagePS> _requestsByServer = new();

    public RequestsSectionPS(ILogger<RequestsSectionPS> logger, SyncMediator mediator, UiSharedService uiSharedService,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager) : base(logger, mediator)
    {
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _pairManager = pairManager;

        Mediator.Subscribe<RequestsChangedMessagePS>(this, msg => _requestsByServer[msg.ServerIndex] = msg);
        Mediator.Subscribe<DisconnectedMessage>(this, msg => _requestsByServer.TryRemove(msg.ServerIndex, out _));
        // The UI can be created after a server already sent its lists
        Mediator.Publish(new RequestsRefreshMessagePS());
    }

    public int PendingCount => _requestsByServer.Values.Sum(s => s.PairRequests.Count + s.GroupInvites.Count);

    public void Draw()
    {
        foreach (var server in _requestsByServer.Values.OrderBy(s => s.ServerIndex))
        {
            var serverName = _serverConfigurationManager.GetServerByIndex(server.ServerIndex).ServerName;

            foreach (var requestor in server.PairRequests.Select(r => r.Requestor))
            {
                var uid = requestor.UID;
                using var id = ImRaii.PushId($"ps-pair-{server.ServerIndex}-{uid}");
                DrawRow(FontAwesomeIcon.UserPlus, _pairManager.DescribeUserPS(server.ServerIndex, requestor),
                    serverName,
                    () => Send(server.ServerIndex, RequestActionPS.AcceptPairRequest, uid),
                    () => Send(server.ServerIndex, RequestActionPS.DismissPairRequest, uid));
            }

            foreach (var invite in server.GroupInvites)
            {
                var from = invite.InvitingUser != null ? _pairManager.DescribeUserPS(server.ServerIndex, invite.InvitingUser) : "someone";
                using var id = ImRaii.PushId($"ps-invite-{server.ServerIndex}-{invite.RequestId}");
                DrawRow(FontAwesomeIcon.Users, $"Syncshell {invite.Group.AliasOrGID}",
                    $"Invite from {from} on {serverName}",
                    () => Send(server.ServerIndex, RequestActionPS.AcceptGroupInvite, invite.RequestId),
                    () => Send(server.ServerIndex, RequestActionPS.DismissGroupInvite, invite.RequestId));
            }
        }
    }

    private void DrawRow(FontAwesomeIcon icon, string text, string tooltip, Action accept, Action dismiss)
    {
        ImGui.AlignTextToFramePadding();
        _uiSharedService.IconText(icon, ImGuiColors.DalamudYellow);
        UiSharedService.AttachToolTip(tooltip);
        ImGui.SameLine();
        ImGui.TextUnformatted(text);
        UiSharedService.AttachToolTip(tooltip);

        var buttonWidth = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Check).X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();

        ImGui.SameLine(windowEndX - buttonWidth * 2 - spacing);
        if (_uiSharedService.IconButton(FontAwesomeIcon.Check)) accept();
        UiSharedService.AttachToolTip("Accept");
        ImGui.SameLine();
        if (_uiSharedService.IconButton(FontAwesomeIcon.Times)) dismiss();
        UiSharedService.AttachToolTip("Dismiss");
    }

    private void Send(int serverIndex, RequestActionPS action, string id)
        => Mediator.Publish(new RequestActionMessagePS(serverIndex, action, id));
}
