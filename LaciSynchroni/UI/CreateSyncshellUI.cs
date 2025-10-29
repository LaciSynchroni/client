using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LaciSynchroni.Common.Data.Extensions;
using LaciSynchroni.Common.Dto.Group;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.UI.Components;
using LaciSynchroni.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace LaciSynchroni.UI;

public class CreateSyncshellUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly ServerSelectorSmall _serverSelector;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PairManager _pairManager;
    private bool _errorGroupCreate;
    private GroupJoinDto? _lastCreatedGroup;
    private int _serverIndexForCreation = 0;

    public CreateSyncshellUI(ILogger<CreateSyncshellUI> logger, SyncMediator syncMediator, ApiController apiController, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService, ServerConfigurationManager serverConfigurationManager, PairManager pairManager)
        : base(logger, syncMediator, "Create new Syncshell###LaciSynchroniCreateSyncshell", performanceCollectorService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _pairManager = pairManager;
        _serverSelector = new ServerSelectorSmall(newIndex => _serverIndexForCreation = newIndex);
        SizeConstraints = new()
        {
            MinimumSize = new(550, 350),
            MaximumSize = new(550, 350)
        };

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            // Only disconnect if we have nothing left to create for. The selector will auto-swap to the next available server.
            if (_apiController.ConnectedServerIndexes.Length <= 0)
            {
                IsOpen = false;
            }
        });
    }

    protected override void DrawInternal()
    {
        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted("Create new Syncshell");

        if (_lastCreatedGroup == null)
        {
            _serverSelector.Draw(_serverConfigurationManager.GetServerInfo(), _apiController.ConnectedServerIndexes, 300f);
            UiSharedService.AttachToolTip("Server to create the Syncshell for. Only connected servers can be selected.");
            ImGui.SameLine();
            var maxGroupsCreateable = _apiController.GetMaxGroupsCreatedByUser(_serverIndexForCreation);
            var currentUserUid = _apiController.GetUidByServer(_serverIndexForCreation);
            using (ImRaii.Disabled(_pairManager.GroupPairs.Select(k => k.Key).Distinct()
                                       .Count(g => string.Equals(g.GroupFullInfo.OwnerUID, currentUserUid,
                                           StringComparison.Ordinal)) >=
                                   maxGroupsCreateable))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create Syncshell"))
                {
                    try
                    {
                        _lastCreatedGroup = _apiController.GroupCreate(_serverIndexForCreation).Result;
                    }
                    catch
                    {
                        _lastCreatedGroup = null;
                        _errorGroupCreate = true;
                    }
                }
                ImGui.SameLine();
            }
        }

        ImGui.Separator();

        if (_lastCreatedGroup == null)
        {
            var defaultPermissions = _apiController.GetDefaultPermissionsForServer(_serverIndexForCreation);
            var serverInfo = _apiController.GetServerInfoForServer(_serverIndexForCreation);
            UiSharedService.TextWrapped("Creating a new Syncshell will create it with your current preferred permissions for Syncshells as default suggested permissions." + Environment.NewLine +
                "- You can own up to " + serverInfo?.MaxGroupsCreatedByUser + " Syncshells on this server." + Environment.NewLine +
                "- You can join up to " + serverInfo?.MaxGroupsJoinedByUser + " Syncshells on this server (including your own)" + Environment.NewLine +
                "- Syncshells on this server can have a maximum of " + serverInfo?.MaxGroupUserCount + " users");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("Your current Syncshell preferred permissions are:");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Animations");
            _uiSharedService.BooleanToColoredIcon(!defaultPermissions!.DisableGroupAnimations);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Sounds");
            _uiSharedService.BooleanToColoredIcon(!defaultPermissions!.DisableGroupSounds);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- VFX");
            _uiSharedService.BooleanToColoredIcon(!defaultPermissions!.DisableGroupVFX);
            UiSharedService.TextWrapped("(Those preferred permissions can be changed anytime after Syncshell creation, your defaults can be changed anytime in the Laci Synchroni Settings)");
        }
        else
        {
            _errorGroupCreate = false;
            ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.Group.GID);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell Password: " + _lastCreatedGroup.Password);
            ImGui.SameLine();
            if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_lastCreatedGroup.Password);
            }
            UiSharedService.TextWrapped("You can change the Syncshell password later at any time.");
            ImGui.Separator();
            UiSharedService.TextWrapped("These settings were set based on your preferred syncshell permissions:");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Animation sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableAnimations());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Sounds sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableSounds());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest VFX sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableVFX());
        }

        if (_errorGroupCreate)
        {
            UiSharedService.ColorTextWrapped("Something went wrong during creation of a new Syncshell", new Vector4(1, 0, 0, 1));
        }
    }

    public override void OnOpen()
    {
        _lastCreatedGroup = null;
    }
}