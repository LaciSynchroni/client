using LaciSynchroni.Common.Data;
using MessagePack;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// The parts of Lightless's UserProfileDto (LightlessSync.API.Dto.User.UserProfileDto) that Laci can show.
/// Lightless sends the profile picture as a URL instead of base64. Other fields Lightless sends (banner, portrait,
/// colours, tags, ...) are ignored. Only used inside <see cref="SyncHubClientLL"/>.
/// <see cref="Disabled"/> and <see cref="ProfilePictureBase64"/> are the older, Mare-style fields, kept in case
/// the server still sends them.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record UserProfileDtoLL(UserData User, bool IsModerationDisabled = false, bool? IsNSFW = null, string? Description = null,
    string? ProfilePictureUrl = null, bool Disabled = false, string? ProfilePictureBase64 = null);
