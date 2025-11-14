using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using LaciSynchroni.Interop.Ipc;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.Extensions.Logging;
using NotificationMessage = LaciSynchroni.Services.Mediator.NotificationMessage;
using System.Threading;

namespace LaciSynchroni.UI;

/// <summary>
/// UI window that shows a confirmation dialog when a user clicks a server join link
/// </summary>
internal class ServerJoinConfirmationUI : WindowMediatorSubscriberBase
{
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly ILogger<ServerJoinConfirmationUI> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    
    private ServerStorage? _pendingServer = null;
    private int _addedServerIndex = -1;
    private bool _isAuthenticating = false;
    private CancellationTokenSource _authCts = new();
    
    // OAuth flow state
    private Task<Uri?>? _oauthCheckTask;
    private Task<string?>? _oauthTokenTask;
    private Task<Dictionary<string, string>>? _oauthUidsTask;

    public ServerJoinConfirmationUI(
        ILogger<ServerJoinConfirmationUI> logger, 
        SyncMediator mediator,
        ServerConfigurationManager serverConfigurationManager,
        UiSharedService uiSharedService,
        DalamudUtilService dalamudUtil,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Add Laci Synchroni Server###LaciSynchroniServerJoinConfirmation", performanceCollectorService)
    {
        _logger = logger;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _dalamudUtil = dalamudUtil;
        
        SizeConstraints = new()
        {
            MinimumSize = new(600, 400),
            MaximumSize = new(800, 700)
        };

        Flags = ImGuiWindowFlags.NoCollapse;

        // Subscribe to server join requests
        Mediator.Subscribe<ServerJoinRequestMessage>(this, HandleServerJoinRequest);
    }

    private void HandleServerJoinRequest(ServerJoinRequestMessage message)
    {
        _pendingServer = message.ServerStorage;
        _isAuthenticating = false;
        _addedServerIndex = -1;
        _oauthCheckTask = null;
        _oauthTokenTask = null;
        _oauthUidsTask = null;
        IsOpen = true;
    }

    public override void OnOpen()
    {
        // Reset if no pending server
        if (_pendingServer == null)
        {
            IsOpen = false;
        }
    }

    public override void OnClose()
    {
        _authCts?.Cancel();
        _authCts = new CancellationTokenSource();
        _isAuthenticating = false;
        _addedServerIndex = -1;
        base.OnClose();
    }

    protected override void DrawInternal()
    {
        if (_pendingServer == null)
        {
            IsOpen = false;
            return;
        }

        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted("Add New Server");
        ImGui.Separator();

        UiSharedService.ColorTextWrapped(
            "You have clicked a link to add a new Laci Synchroni server. " +
            "Please review the server information below and click 'Add Server' to continue.",
            ImGuiColors.HealerGreen);

        ImGuiHelpers.ScaledDummy(5f);

        ImGui.TextUnformatted("Server Information:");
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        // Display server details
        DrawServerDetail("Server Name:", _pendingServer.ServerName);
        DrawServerDetail("Server URI:", _pendingServer.ServerUri);
        
        if (_pendingServer.UseAdvancedUris)
        {
            if (!string.IsNullOrEmpty(_pendingServer.ServerHubUri))
            {
                DrawServerDetail("Hub URI:", _pendingServer.ServerHubUri);
            }
            if (!string.IsNullOrEmpty(_pendingServer.AuthUri))
            {
                DrawServerDetail("Auth URI:", _pendingServer.AuthUri);
            }
        }
        
        DrawServerDetail("Authentication:", _pendingServer.UseOAuth2 ? "OAuth2 (Discord)" : "Secret Key");
        
        if (_pendingServer.BypassVersionCheck)
        {
            ImGuiHelpers.ScaledDummy(2f);
            UiSharedService.ColorTextWrapped(
                "This server link has version check bypass enabled. This may cause unexpected behavior if the server is not compatible.",
                ImGuiColors.DalamudYellow);
        }

        ImGuiHelpers.ScaledDummy(5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(2f);

        // Action buttons
        if (!_isAuthenticating)
        {
            var buttonWidth = 100f * ImGuiHelpers.GlobalScale;
            var spacing = 10f * ImGuiHelpers.GlobalScale;
            var totalWidth = (buttonWidth * 2) + spacing;
            
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) / 2);

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add Server"))
            {
                try
                {
                    _serverConfigurationManager.AddServer(_pendingServer);
                    _addedServerIndex = _serverConfigurationManager.GetServerInfo().Count - 1;
                    _logger.LogInformation("Added server via link: {ServerName} at index {Index}", _pendingServer.ServerName, _addedServerIndex);
                    
                    // If OAuth server, start authentication flow
                    if (_pendingServer.UseOAuth2)
                    {
                        _isAuthenticating = true;
                        _oauthCheckTask = _serverConfigurationManager.CheckDiscordOAuth(_pendingServer.ServerUri);
                    }
                    else
                    {
                        Mediator.Publish(new NotificationMessage(
                            "Server Added", 
                            $"Successfully added '{_pendingServer.ServerName}'. Configure your secret key in Settings.",
                            NotificationType.Info));
                        
                        _pendingServer = null;
                        IsOpen = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add server: {ServerName}", _pendingServer.ServerName);
                    Mediator.Publish(new NotificationMessage(
                        "Error", 
                        "Failed to add server. Please try again or add it manually in Settings.",
                        NotificationType.Error));
                }
            }

            ImGui.SameLine();

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Cancel"))
            {
                _pendingServer = null;
                IsOpen = false;
            }
        }

        // Handle OAuth authentication flow
        if (_isAuthenticating)
        {
            DrawOAuthFlow();
            
            // Show cancel button during OAuth, but hide it when we reach the final success state
            // (when all OAuth tasks are complete and successful)
            bool showCancelButton = !(_oauthUidsTask?.IsCompleted == true && _oauthUidsTask.Result?.Count > 0);
            
            if (showCancelButton)
            {
                ImGuiHelpers.ScaledDummy(3f);
                ImGui.Separator();
                ImGuiHelpers.ScaledDummy(2f);
                
                var buttonWidth = 100f * ImGuiHelpers.GlobalScale;
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) / 2);
                
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "Cancel"))
                {
                    _authCts.Cancel();
                    _pendingServer = null;
                    IsOpen = false;
                }
            }
        }
    }

    private void DrawServerDetail(string label, string value)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(150 * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudWhite))
        {
            ImGui.TextUnformatted(value);
        }
    }

    private void DrawOAuthFlow()
    {
        ImGuiHelpers.ScaledDummy(5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5f);

        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted("Authentication Setup");

        ImGuiHelpers.ScaledDummy(2f);

        // Step 1: Check OAuth support
        if (_oauthCheckTask == null)
        {
            return;
        }

        if (!_oauthCheckTask.IsCompleted)
        {
            UiSharedService.ColorTextWrapped("Checking server OAuth support...", ImGuiColors.DalamudYellow);
            return;
        }

        var oauthUri = _oauthCheckTask.Result;
        if (oauthUri == null)
        {
            UiSharedService.ColorTextWrapped("This server doesn't support OAuth or is unreachable. You'll need to configure manually in Settings.", ImGuiColors.DalamudRed);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Finish"))
            {
                _pendingServer = null;
                IsOpen = false;
            }
            return;
        }

        // Step 2: Get OAuth token
        if (_oauthTokenTask == null)
        {
            UiSharedService.ColorTextWrapped("OAuth supported", ImGuiColors.HealerGreen);
            ImGuiHelpers.ScaledDummy(2f);
            UiSharedService.TextWrapped("Click the button below to authenticate with Discord. A browser window will open.");
            ImGuiHelpers.ScaledDummy(2f);
            
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Authenticate with Discord"))
            {
                var server = _serverConfigurationManager.GetServerByIndex(_addedServerIndex);
                _oauthTokenTask = _serverConfigurationManager.GetDiscordOAuthToken(oauthUri, server.ServerUri, _authCts.Token);
            }
            return;
        }

        if (!_oauthTokenTask.IsCompleted)
        {
            UiSharedService.ColorTextWrapped("Waiting for Discord authentication...", ImGuiColors.DalamudYellow);
            UiSharedService.TextWrapped("Follow the browser window to complete authentication. This may take up to 60 seconds.");
            return;
        }

        var token = _oauthTokenTask.Result;
        if (string.IsNullOrEmpty(token))
        {
            UiSharedService.ColorTextWrapped("Authentication failed or timed out. You can try again in Settings.", ImGuiColors.DalamudRed);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Finish"))
            {
                _pendingServer = null;
                IsOpen = false;
            }
            return;
        }

        // Save token
        var addedServer = _serverConfigurationManager.GetServerByIndex(_addedServerIndex);
        if (string.IsNullOrEmpty(addedServer.OAuthToken))
        {
            addedServer.OAuthToken = token;
            _serverConfigurationManager.Save();
        }

        // Step 3: Get UIDs
        if (_oauthUidsTask == null)
        {
            UiSharedService.ColorTextWrapped("Discord authentication successful", ImGuiColors.HealerGreen);
            _oauthUidsTask = _serverConfigurationManager.GetUIDsWithDiscordToken(addedServer.ServerUri, token);
            return;
        }

        if (!_oauthUidsTask.IsCompleted)
        {
            UiSharedService.ColorTextWrapped("Retrieving your characters...", ImGuiColors.DalamudYellow);
            return;
        }

        var uids = _oauthUidsTask.Result;
        if (uids == null || uids.Count == 0)
        {
            UiSharedService.ColorTextWrapped("No characters found. You may need to register your characters on the server website first.", ImGuiColors.DalamudRed);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Finish"))
            {
                _pendingServer = null;
                IsOpen = false;
            }
            return;
        }

        // Step 4: Auto-assign current character
        try
        {
            var currentCharName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
            var currentWorldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
            var currentCid = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult();

            // Check if already assigned
            var existingAuth = addedServer.Authentications.FirstOrDefault(a => 
                string.Equals(a.CharacterName, currentCharName, StringComparison.Ordinal) && 
                a.WorldId == currentWorldId);

            if (existingAuth == null)
            {
                // Auto-assign first UID to current character
                var firstUid = uids.First();
                addedServer.Authentications.Add(new Authentication
                {
                    CharacterName = currentCharName,
                    WorldId = currentWorldId,
                    LastSeenCID = currentCid,
                    UID = firstUid.Key,
                    SecretKeyIdx = -1
                });
                _serverConfigurationManager.Save();

                UiSharedService.ColorTextWrapped($"Successfully configured!", ImGuiColors.HealerGreen);
                UiSharedService.TextWrapped($"Your character '{currentCharName}' has been assigned UID: {firstUid.Key}");
                
                if (uids.Count > 1)
                {
                    ImGuiHelpers.ScaledDummy(2f);
                    UiSharedService.ColorTextWrapped($"Note: You have {uids.Count} characters available. Visit Settings to configure additional characters.", ImGuiColors.DalamudYellow);
                }
            }
            else if (string.IsNullOrEmpty(existingAuth.UID))
            {
                // Assign UID to existing auth
                existingAuth.UID = uids.First().Key;
                _serverConfigurationManager.Save();
                
                UiSharedService.ColorTextWrapped($"Successfully configured!", ImGuiColors.HealerGreen);
                UiSharedService.TextWrapped($"Your character '{currentCharName}' has been assigned UID: {existingAuth.UID}");
            }
            else
            {
                UiSharedService.ColorTextWrapped($"Character already configured!", ImGuiColors.HealerGreen);
            }

            ImGuiHelpers.ScaledDummy(3f);
            
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Finish & Connect"))
            {
                // Initiate connection to the newly added server
                _ = Task.Run(async () => await _uiSharedService.ApiController.CreateConnectionsAsync(_addedServerIndex));
                
                Mediator.Publish(new NotificationMessage(
                    "Connecting", 
                    $"Connecting to '{_pendingServer?.ServerName}'...",
                    NotificationType.Info));
                
                _pendingServer = null;
                IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-assign character");
            UiSharedService.ColorTextWrapped("Failed to assign character. Please configure manually in Settings.", ImGuiColors.DalamudRed);
            
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Finish"))
            {
                _pendingServer = null;
                IsOpen = false;
            }
        }
    }
}

