using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using LaciSynchroni.Common.Data.Enum;
using LaciSynchroni.Common.Data.Extensions;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.UI.Components;
using LaciSynchroni.WebAPI;
using System.Numerics;

namespace LaciSynchroni.UI;

public class TopTabMenu
{
    private readonly ApiController _apiController;
    private readonly SyncMediator _syncMediator;
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ServerSelectorSmall _pairTabServerSelector;

    private string _filter = string.Empty;
    private int _globalControlCountdown = 0;

    private string _pairToAdd = string.Empty;
    private int _pairTabSelectedServer = 0;

    private SelectedTab _selectedTab = SelectedTab.None;
    public TopTabMenu(SyncMediator syncMediator, ApiController apiController, PairManager pairManager, UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager)
    {
        _syncMediator = syncMediator;
        _apiController = apiController;
        _pairManager = pairManager;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _pairTabServerSelector = new ServerSelectorSmall(index => _pairTabSelectedServer = index);
    }

    public bool IsUserConfigTabSelected => TabSelection == SelectedTab.UserConfig;

    private enum SelectedTab
    {
        None,
        Individual,
        Syncshell,
        Filter,
        UserConfig
    }

    public string Filter
    {
        get => _filter;
        private set
        {
            if (!string.Equals(_filter, value, StringComparison.OrdinalIgnoreCase))
            {
                _syncMediator.Publish(new RefreshUiMessage());
            }

            _filter = value;
        }
    }
    private SelectedTab TabSelection
    {
        get => _selectedTab; set
        {
            if (_selectedTab == SelectedTab.Filter && value != SelectedTab.Filter)
            {
                Filter = string.Empty;
            }

            _selectedTab = value;
        }
    }
    public void Draw()
    {
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var buttonX = (availableWidth - (spacing.X * 3)) / 4f;
        var buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y;
        var buttonSize = new Vector2(buttonX, buttonY);
        var drawList = ImGui.GetWindowDrawList();
        var underlineColor = ImGui.GetColorU32(ImGuiCol.Separator);
        var btncolor = ImRaii.PushColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new(0, 0, 0, 0)));

        ImGuiHelpers.ScaledDummy(spacing.Y / 2f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.BeginDisabled(!_apiController.AnyServerConnected);
            var x = ImGui.GetCursorScreenPos();
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), buttonSize))
            {
                TabSelection = TabSelection == SelectedTab.Individual ? SelectedTab.None : SelectedTab.Individual;
            }
            ImGui.SameLine();
            var xAfter = ImGui.GetCursorScreenPos();
            if (TabSelection == SelectedTab.Individual)
                drawList.AddLine(x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xAfter with { Y = xAfter.Y + buttonSize.Y + spacing.Y, X = xAfter.X - spacing.X },
                    underlineColor, 2);
            ImGui.EndDisabled();
        }
        UiSharedService.AttachToolTip("Individual Pair Menu");

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.BeginDisabled(!_apiController.AnyServerConnected);
            var x = ImGui.GetCursorScreenPos();
            if (ImGui.Button(FontAwesomeIcon.Users.ToIconString(), buttonSize))
            {
                TabSelection = TabSelection == SelectedTab.Syncshell ? SelectedTab.None : SelectedTab.Syncshell;
            }
            ImGui.SameLine();
            var xAfter = ImGui.GetCursorScreenPos();
            if (TabSelection == SelectedTab.Syncshell)
                drawList.AddLine(x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xAfter with { Y = xAfter.Y + buttonSize.Y + spacing.Y, X = xAfter.X - spacing.X },
                    underlineColor, 2);
            ImGui.EndDisabled();
        }
        UiSharedService.AttachToolTip("Syncshell Menu");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.BeginDisabled(!_apiController.AnyServerConnected);
            var x = ImGui.GetCursorScreenPos();
            if (ImGui.Button(FontAwesomeIcon.Filter.ToIconString(), buttonSize))
            {
                TabSelection = TabSelection == SelectedTab.Filter ? SelectedTab.None : SelectedTab.Filter;
            }

            ImGui.SameLine();
            var xAfter = ImGui.GetCursorScreenPos();
            if (TabSelection == SelectedTab.Filter)
                drawList.AddLine(x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xAfter with { Y = xAfter.Y + buttonSize.Y + spacing.Y, X = xAfter.X - spacing.X },
                    underlineColor, 2);
            ImGui.EndDisabled();
        }
        UiSharedService.AttachToolTip("Filter");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var x = ImGui.GetCursorScreenPos();
            if (ImGui.Button(FontAwesomeIcon.UserCog.ToIconString(), buttonSize))
            {
                TabSelection = TabSelection == SelectedTab.UserConfig ? SelectedTab.None : SelectedTab.UserConfig;
            }

            ImGui.SameLine();
            var xAfter = ImGui.GetCursorScreenPos();
            if (TabSelection == SelectedTab.UserConfig)
                drawList.AddLine(x with { Y = x.Y + buttonSize.Y + spacing.Y },
                    xAfter with { Y = xAfter.Y + buttonSize.Y + spacing.Y, X = xAfter.X - spacing.X },
                    underlineColor, 2);
        }
        UiSharedService.AttachToolTip("Your User Menu");

        ImGui.NewLine();
        btncolor.Dispose();

        ImGuiHelpers.ScaledDummy(spacing);

        if (TabSelection == SelectedTab.Individual)
        {
            DrawAddPair(availableWidth, spacing.X);
            DrawGlobalIndividualButtons(availableWidth, spacing.X);
        }
        else if (TabSelection == SelectedTab.Syncshell)
        {
            DrawSyncshellMenu(availableWidth, spacing.X);
            DrawGlobalSyncshellButtons(availableWidth, spacing.X);
        }
        else if (TabSelection == SelectedTab.Filter)
        {
            DrawFilter(availableWidth, spacing.X);
        }
        else if (TabSelection == SelectedTab.UserConfig)
        {
            DrawUserConfig(availableWidth, spacing.X);
        }

        if (TabSelection != SelectedTab.None) ImGuiHelpers.ScaledDummy(3f);
        ImGui.Separator();
    }

    private void DrawAddPair(float availableXWidth, float spacingX)
    {
        _pairTabServerSelector.Draw(_serverConfigurationManager.GetServerInfo(), _apiController.ConnectedServerIndexes, availableXWidth);
        UiSharedService.AttachToolTip("Server to use for pair actions");

        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.UserPlus, "Add");
        ImGui.SetNextItemWidth(availableXWidth - buttonSize - spacingX);
        ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref _pairToAdd, 20);
        ImGui.SameLine();
        var alreadyExisting = _pairManager.DirectPairs.Exists(p => string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal));
        using (ImRaii.Disabled(alreadyExisting || string.IsNullOrEmpty(_pairToAdd)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserPlus, "Add"))
            {
                // Adds pair for the current
                _ = _apiController.UserAddPairToServer(_pairTabSelectedServer, _pairToAdd);
                _pairToAdd = string.Empty;
            }
        }
        UiSharedService.AttachToolTip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd));
    }

    private void DrawFilter(float availableWidth, float spacingX)
    {
        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Ban, "Clear");
        ImGui.SetNextItemWidth(availableWidth - buttonSize - spacingX);
        string filter = Filter;
        if (ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref filter, 255))
        {
            Filter = filter;
        }
        ImGui.SameLine();
        using var disabled = ImRaii.Disabled(string.IsNullOrEmpty(Filter));
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear"))
        {
            Filter = string.Empty;
        }
    }

    private void DrawGlobalIndividualButtons(float availableXWidth, float spacingX)
    {
        var buttonX = (availableXWidth - (spacingX * 3)) / 4f;
        var buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y;
        var buttonSize = new Vector2(buttonX, buttonY);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Pause.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Individual Pause");
            }
        }
        UiSharedService.AttachToolTip("Globally resume or pause all individual pairs." + UiSharedService.TooltipSeparator
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.VolumeUp.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Individual Sounds");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable sound sync with all individual pairs."
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Running.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Individual Animations");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable animation sync with all individual pairs." + UiSharedService.TooltipSeparator
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Sun.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Individual VFX");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable VFX sync with all individual pairs." + UiSharedService.TooltipSeparator
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));


        PopupIndividualSetting("Individual Pause", "Unpause all individuals", "Pause all individuals",
            FontAwesomeIcon.Play, FontAwesomeIcon.Pause,
            (perm) =>
            {
                perm.SetPaused(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetPaused(true);
                return perm;
            });
        PopupIndividualSetting("Individual Sounds", "Enable sounds for all individuals", "Disable sounds for all individuals",
            FontAwesomeIcon.VolumeUp, FontAwesomeIcon.VolumeMute,
            (perm) =>
            {
                perm.SetDisableSounds(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableSounds(true);
                return perm;
            });
        PopupIndividualSetting("Individual Animations", "Enable animations for all individuals", "Disable animations for all individuals",
            FontAwesomeIcon.Running, FontAwesomeIcon.Stop,
            (perm) =>
            {
                perm.SetDisableAnimations(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableAnimations(true);
                return perm;
            });
        PopupIndividualSetting("Individual VFX", "Enable VFX for all individuals", "Disable VFX for all individuals",
            FontAwesomeIcon.Sun, FontAwesomeIcon.Circle,
            (perm) =>
            {
                perm.SetDisableVFX(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableVFX(true);
                return perm;
            });
    }

    private void DrawGlobalSyncshellButtons(float availableXWidth, float spacingX)
    {
        var buttonX = (availableXWidth - (spacingX * 4)) / 5f;
        var buttonY = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Pause).Y;
        var buttonSize = new Vector2(buttonX, buttonY);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Pause.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Syncshell Pause");
            }
        }
        UiSharedService.AttachToolTip("Globally resume or pause all syncshells." + UiSharedService.TooltipSeparator
                        + "Note: This will not affect users with preferred permissions in syncshells."
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.VolumeUp.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Syncshell Sounds");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable sound sync with all syncshells." + UiSharedService.TooltipSeparator
                        + "Note: This will not affect users with preferred permissions in syncshells."
                        + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Running.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Syncshell Animations");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable animation sync with all syncshells." + UiSharedService.TooltipSeparator
                        + "Note: This will not affect users with preferred permissions in syncshells."
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0);

            if (ImGui.Button(FontAwesomeIcon.Sun.ToIconString(), buttonSize))
            {
                ImGui.OpenPopup("Syncshell VFX");
            }
        }
        UiSharedService.AttachToolTip("Globally enable or disable VFX sync with all syncshells." + UiSharedService.TooltipSeparator
                        + "Note: This will not affect users with preferred permissions in syncshells."
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));


        PopupSyncshellSetting("Syncshell Pause", "Unpause all syncshells", "Pause all syncshells",
            FontAwesomeIcon.Play, FontAwesomeIcon.Pause,
            (perm) =>
            {
                perm.SetPaused(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetPaused(true);
                return perm;
            });
        PopupSyncshellSetting("Syncshell Sounds", "Enable sounds for all syncshells", "Disable sounds for all syncshells",
            FontAwesomeIcon.VolumeUp, FontAwesomeIcon.VolumeMute,
            (perm) =>
            {
                perm.SetDisableSounds(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableSounds(true);
                return perm;
            });
        PopupSyncshellSetting("Syncshell Animations", "Enable animations for all syncshells", "Disable animations for all syncshells",
            FontAwesomeIcon.Running, FontAwesomeIcon.Stop,
            (perm) =>
            {
                perm.SetDisableAnimations(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableAnimations(true);
                return perm;
            });
        PopupSyncshellSetting("Syncshell VFX", "Enable VFX for all syncshells", "Disable VFX for all syncshells",
            FontAwesomeIcon.Sun, FontAwesomeIcon.Circle,
            (perm) =>
            {
                perm.SetDisableVFX(false);
                return perm;
            },
            (perm) =>
            {
                perm.SetDisableVFX(true);
                return perm;
            });

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var disabled = ImRaii.Disabled(_globalControlCountdown > 0 || !UiSharedService.CtrlPressed());

            if (ImGui.Button(FontAwesomeIcon.Check.ToIconString(), buttonSize))
            {
                _ = GlobalControlCountdown(10);
                var bulkSyncshells = _pairManager.GroupPairs.Keys.OrderBy(g => g.GroupFullInfo.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.GroupFullInfo.Group.GID, g =>
                    {
                        var perm = g.GroupFullInfo.GroupUserPermissions;
                        perm.SetDisableSounds(g.GroupFullInfo.GroupPermissions.IsPreferDisableSounds());
                        perm.SetDisableAnimations(g.GroupFullInfo.GroupPermissions.IsPreferDisableAnimations());
                        perm.SetDisableVFX(g.GroupFullInfo.GroupPermissions.IsPreferDisableVFX());
                        return perm;
                    }, StringComparer.Ordinal);

                _ = _apiController.SetBulkPermissions(_pairTabSelectedServer, new(new(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(false);
            }
        }
        UiSharedService.AttachToolTip("Globally align syncshell permissions to suggested syncshell permissions." + UiSharedService.TooltipSeparator
            + "Note: This will not affect users with preferred permissions in syncshells." + Environment.NewLine
            + "Note: If multiple users share one syncshell the permissions to that user will be set to " + Environment.NewLine
            + "the ones of the last applied syncshell in alphabetical order." + UiSharedService.TooltipSeparator
            + "Hold CTRL to enable this button"
            + (_globalControlCountdown > 0 ? UiSharedService.TooltipSeparator + "Available again in " + _globalControlCountdown + " seconds." : string.Empty));
    }

    private void DrawSyncshellMenu(float availableWidth, float spacingX)
    {
        var buttonX = (availableWidth - (spacingX)) / 2f;

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create new Syncshell", buttonX))
        {
            _syncMediator.Publish(new UiToggleMessage(typeof(CreateSyncshellUI)));
        }
        ImGui.SameLine();

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Join existing Syncshell", buttonX))
        {
            _syncMediator.Publish(new UiToggleMessage(typeof(JoinSyncshellUI)));
        }
    }

    private void DrawUserConfig(float availableWidth, float spacingX)
    {
        var buttonX = (availableWidth - spacingX) / 2f;
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Chara Data Analysis", buttonX))
        {
            _syncMediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        UiSharedService.AttachToolTip("View and analyze your generated character data");

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Running, "Character Data Hub", availableWidth))
        {
            _syncMediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }

        ImGui.BeginDisabled(!_apiController.AnyServerConnected);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserCircle, "Edit Profile", buttonX))
        {
            _syncMediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
        }
        UiSharedService.AttachToolTip("Edit your Laci Synchroni Profile");
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Satellite, "Connected Servers", availableWidth))
        {
            _syncMediator.Publish(new ToggleServerSelectMessage());
        }
        UiSharedService.AttachToolTip("Toggle the server connections list");
    }

    private async Task GlobalControlCountdown(int countdown)
    {
#if DEBUG
        return;
#endif

        _globalControlCountdown = countdown;
        while (_globalControlCountdown > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            _globalControlCountdown--;
        }
    }

    private void PopupIndividualSetting(string popupTitle, string enableText, string disableText,
                    FontAwesomeIcon enableIcon, FontAwesomeIcon disableIcon,
        Func<UserPermissions, UserPermissions> actEnable, Func<UserPermissions, UserPermissions> actDisable)
    {
        if (ImGui.BeginPopup(popupTitle))
        {
            if (_uiSharedService.IconTextButton(enableIcon, enableText, null, true))
            {
                _ = GlobalControlCountdown(10);
                var bulkIndividualPairs = _pairManager.PairsWithGroups.Keys
                    .Where(g => g.IndividualPairStatus == IndividualPairStatus.Bidirectional)
                    .ToDictionary(g => g.UserPair.User.UID, g =>
                    {
                        return actEnable(g.UserPair.OwnPermissions);
                    }, StringComparer.Ordinal);

                _ = _apiController.SetBulkPermissions(_pairTabSelectedServer, new(bulkIndividualPairs, new(StringComparer.Ordinal))).ConfigureAwait(false);
                ImGui.CloseCurrentPopup();
            }

            if (_uiSharedService.IconTextButton(disableIcon, disableText, null, true))
            {
                _ = GlobalControlCountdown(10);
                var bulkIndividualPairs = _pairManager.PairsWithGroups.Keys
                    .Where(g => g.IndividualPairStatus == IndividualPairStatus.Bidirectional)
                    .ToDictionary(g => g.UserPair.User.UID, g =>
                    {
                        return actDisable(g.UserPair.OwnPermissions);
                    }, StringComparer.Ordinal);
                _ = _apiController.SetBulkPermissions(_pairTabSelectedServer, new(bulkIndividualPairs, new(StringComparer.Ordinal))).ConfigureAwait(false);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
    private void PopupSyncshellSetting(string popupTitle, string enableText, string disableText,
        FontAwesomeIcon enableIcon, FontAwesomeIcon disableIcon,
        Func<GroupUserPreferredPermissions, GroupUserPreferredPermissions> actEnable,
        Func<GroupUserPreferredPermissions, GroupUserPreferredPermissions> actDisable)
    {
        if (ImGui.BeginPopup(popupTitle))
        {

            if (_uiSharedService.IconTextButton(enableIcon, enableText, null, true))
            {
                _ = GlobalControlCountdown(10);
                var bulkSyncshells = _pairManager.GroupPairs.Keys
                    .OrderBy(u => u.GroupFullInfo.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.GroupFullInfo.Group.GID, g =>
                    {
                        return actEnable(g.GroupFullInfo.GroupUserPermissions);
                    }, StringComparer.Ordinal);

                _ = _apiController.SetBulkPermissions(_pairTabSelectedServer, new(new(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(false);
                ImGui.CloseCurrentPopup();
            }

            if (_uiSharedService.IconTextButton(disableIcon, disableText, null, true))
            {
                _ = GlobalControlCountdown(10);
                var bulkSyncshells = _pairManager.GroupPairs.Keys
                    .OrderBy(u => u.GroupFullInfo.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.GroupFullInfo.Group.GID, g =>
                    {
                        return actDisable(g.GroupFullInfo.GroupUserPermissions);
                    }, StringComparer.Ordinal);

                _ = _apiController.SetBulkPermissions(_pairTabSelectedServer, new(new(StringComparer.Ordinal), bulkSyncshells)).ConfigureAwait(false);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}