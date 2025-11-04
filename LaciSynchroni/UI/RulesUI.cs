using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using LaciSynchroni.Interop.Ipc;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LaciSynchroni.UI;
internal class RulesUI : WindowMediatorSubscriberBase
{
    private readonly ILogger<ServerJoinConfirmationUI> _logger;
    private readonly UiSharedService _uiSharedService;

    private string _rules;

    public RulesUI(
    ILogger<ServerJoinConfirmationUI> logger,
    SyncMediator mediator,
    UiSharedService uiSharedService,
    PerformanceCollectorService performanceCollectorService)
    : base(logger, mediator, "Accept Rules###LaciSynchroniAcceptRulesConfirmation", performanceCollectorService)
    {
        _logger = logger;
        _uiSharedService = uiSharedService;

        SizeConstraints = new()
        {
            MinimumSize = new(600, 500),
        };

        Flags = ImGuiWindowFlags.NoCollapse;

        // Subscribe to server join requests
        Mediator.Subscribe<RulesViewRequestMessage>(this, HandleRulesViewRequest);
    }

    protected override void DrawInternal()
    {
        ImGui.TextUnformatted(_rules);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PenSquare, "Accept Server Rules"))
        {
            Mediator.Publish(new RulesAcceptedMessage());
            IsOpen = false;
        }
    }

    private void HandleRulesViewRequest(RulesViewRequestMessage message)
    {
        _rules = message.rules;
        IsOpen = true;
    }
}

/// <summary>
/// Message published when UI wants to view the rules received by the server
/// </summary>
public record RulesViewRequestMessage(string rules) : MessageBase;

/// <summary>
/// Message published when user has accepted the current rules
/// </summary>
public record RulesAcceptedMessage() : MessageBase;
