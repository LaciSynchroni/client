using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.User;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.UI.Components;
using LaciSynchroni.WebAPI;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LaciSynchroni.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private static readonly float windowWidth = 768;

    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly ProfileManager _profileManager;
    private readonly UiSharedService _uiSharedService;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ServerSelectorSmall _serverSelector;

    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private IDalamudTextureWrap? _pfpTextureWrap;
    private string _profileDescription = string.Empty;
    private byte[] _profileImage = [];
    private bool _showFileDialogError = false;
    private bool _wasOpen;
    private int _serverForProfile = 0;


    public EditProfileUi(ILogger<EditProfileUi> logger, SyncMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        ProfileManager profileManager, PerformanceCollectorService performanceCollectorService, ServerConfigurationManager serverManager)
        : base(logger, mediator, "Laci Synchroni Edit Profile###LaciSynchroniEditProfileUI", performanceCollectorService)
    {
        IsOpen = false;
        this.SizeConstraints = new()
        {
            MinimumSize = new(windowWidth, 512),
            MaximumSize = new(windowWidth, 2000)
        };
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _profileManager = profileManager;
        _serverManager = serverManager;

        _serverSelector = new ServerSelectorSmall(index => _serverForProfile = index);

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            // Only close if we have nothing left to edit for. The selector will auto-swap to the next available server.
            if (_apiController.ConnectedServerIndexes.Length <= 0)
            {
                IsOpen = false;
            }
        });
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            var userUid = _apiController.GetUidByServer(_serverForProfile);
            if (msg.UserData == null || string.Equals(msg.UserData.UserData.UID, userUid, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
            }
        });
    }

    protected override void DrawInternal()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        _serverSelector.Draw(_serverManager.GetServerInfo(), _apiController.ConnectedServerIndexes, windowWidth - spacing);
        _uiSharedService.BigText("Current Profile (as saved on server)");

        // We draw the profile editor for the current server only
        var loggedInUserUID = _apiController.GetUidByServer(_serverForProfile);
        var userData = new ServerBasedUserKey(new UserData(loggedInUserUID), _serverForProfile);
        var profile = _profileManager.GetSyncProfile(userData);

        if (profile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(profile.Description, ImGuiColors.DalamudRed);
            return;
        }

        if (!_profileImage.SequenceEqual(profile.ImageData.Value))
        {
            _profileImage = profile.ImageData.Value;
            _pfpTextureWrap?.Dispose();
            _pfpTextureWrap = _uiSharedService.LoadImage(_profileImage);
        }

        if (!string.Equals(_profileDescription, profile.Description, StringComparison.OrdinalIgnoreCase))
        {
            _profileDescription = profile.Description;
            _descriptionText = _profileDescription;
        }

        if (_pfpTextureWrap != null)
        {
            ImGui.Image(_pfpTextureWrap.Handle, ImGuiHelpers.ScaledVector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
        }

        ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        using (_uiSharedService.GameFont.Push())
        {
            var descriptionTextSize = ImGui.CalcTextSize(profile.Description, wrapWidth: 256f);
            var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 256);
            if (descriptionTextSize.Y > childFrame.Y)
            {
                _adjustedForScollBarsOnlineProfile = true;
            }
            else
            {
                _adjustedForScollBarsOnlineProfile = false;
            }
            childFrame = childFrame with
            {
                X = childFrame.X + (_adjustedForScollBarsOnlineProfile ? ImGui.GetStyle().ScrollbarSize : 0),
            };
            if (ImGui.BeginChildFrame(101, childFrame))
            {
                UiSharedService.TextWrapped(profile.Description);
            }
            ImGui.EndChildFrame();
        }

        var nsfw = profile.IsNSFW;
        ImGui.BeginDisabled();
        ImGui.Checkbox("Is NSFW", ref nsfw);
        ImGui.EndDisabled();

        ImGui.Separator();
        _uiSharedService.BigText("Notes and Rules for Profiles");

        ImGui.TextWrapped($"- All users that are paired and unpaused with you will be able to see your profile picture and description.{Environment.NewLine}" +
            $"- Other users have the possibility to report your profile for breaking the rules.{Environment.NewLine}" +
            $"- !!! AVOID: anything as profile image that can be considered highly illegal or obscene (bestiality, anything that could be considered a sexual act with a minor (that includes Lalafells), etc.){Environment.NewLine}" +
            $"- !!! AVOID: slurs of any kind in the description that can be considered highly offensive{Environment.NewLine}" +
            $"- In case of valid reports from other users this can lead to disabling your profile forever or terminating your Laci account indefinitely.{Environment.NewLine}" +
            $"- Judgement of your profile validity from reports through staff is not up to debate and the decisions to disable your profile/account permanent.{Environment.NewLine}" +
            $"- If your profile picture or profile description could be considered NSFW, enable the toggle below.");
        ImGui.Separator();
        _uiSharedService.BigText("Profile Settings");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile picture"))
        {
            _fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", (success, file) =>
            {
                if (!success) return;
                _ = Task.Run(async () =>
                {
                    var fileContent = File.ReadAllBytes(file);
                    using MemoryStream ms = new(fileContent);
                    var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                    if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                    {
                        _showFileDialogError = true;
                        return;
                    }
                    using var image = Image.Load<Rgba32>(fileContent);

                    if (image.Width > 256 || image.Height > 256 || (fileContent.Length > 250 * 1024))
                    {
                        _showFileDialogError = true;
                        return;
                    }

                    _showFileDialogError = false;
                    await _apiController.UserSetProfile(_serverForProfile, new UserProfileDto(new UserData(loggedInUserUID), Disabled: false, IsNSFW: null, Convert.ToBase64String(fileContent), Description: null))
                        .ConfigureAwait(false);
                });
            });
        }
        UiSharedService.AttachToolTip("Select and upload a new profile picture");
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture"))
        {
            _ = _apiController.UserSetProfile(_serverForProfile, new UserProfileDto(new UserData(loggedInUserUID), Disabled: false, IsNSFW: null, "", Description: null));
        }
        UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped("The profile picture must be a PNG file with a maximum height and width of 256px and 250KiB size", ImGuiColors.DalamudRed);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox("Profile is NSFW", ref isNsfw))
        {
            _ = _apiController.UserSetProfile(_serverForProfile, new UserProfileDto(new UserData(loggedInUserUID), Disabled: false, isNsfw, ProfilePictureBase64: null, Description: null));
        }
        _uiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");
        var widthTextBox = 400;
        var posX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted($"Description {_descriptionText.Length}/1500");
        ImGui.SetCursorPosX(posX);
        ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted("Preview (approximate)");
        using (_uiSharedService.GameFont.Push())
            ImGui.InputTextMultiline("##description", ref _descriptionText, 1500, ImGuiHelpers.ScaledVector2(widthTextBox, 200));

        ImGui.SameLine();

        using (_uiSharedService.GameFont.Push())
        {
            var descriptionTextSizeLocal = ImGui.CalcTextSize(_descriptionText, wrapWidth: 256f);
            var childFrameLocal = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 200);
            if (descriptionTextSizeLocal.Y > childFrameLocal.Y)
            {
                _adjustedForScollBarsLocalProfile = true;
            }
            else
            {
                _adjustedForScollBarsLocalProfile = false;
            }
            childFrameLocal = childFrameLocal with
            {
                X = childFrameLocal.X + (_adjustedForScollBarsLocalProfile ? ImGui.GetStyle().ScrollbarSize : 0),
            };
            if (ImGui.BeginChildFrame(102, childFrameLocal))
            {
                UiSharedService.TextWrapped(_descriptionText);
            }
            ImGui.EndChildFrame();
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Description"))
        {
            _ = _apiController.UserSetProfile(_serverForProfile, new UserProfileDto(new UserData(loggedInUserUID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, _descriptionText));
        }
        UiSharedService.AttachToolTip("Sets your profile description text");
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear Description"))
        {
            _ = _apiController.UserSetProfile(_serverForProfile, new UserProfileDto(new UserData(loggedInUserUID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, ""));
        }
        UiSharedService.AttachToolTip("Clears your profile description text");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}