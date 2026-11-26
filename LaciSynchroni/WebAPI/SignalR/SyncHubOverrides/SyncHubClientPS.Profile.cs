using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.User;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// PlayerSync's profiles: the description holds a JSON profile (see <see cref="ProfileJsonPS"/>), which is shown as
/// readable text, and of which Laci's editor only changes the "about me" text.
/// </summary>
internal partial class SyncHubClientPS
{
    public override async Task<UserProfileDto> UserGetProfile(UserDto dto)
    {
        var profile = await base.UserGetProfile(dto).ConfigureAwait(false);
        if (ProfileJsonPS.TryParse(profile.Description) is not { } json) return profile;

        // Own profile: only the "about me" text, so the profile editor shows and saves just that (see UserSetProfile).
        // Other users: everything Laci can show as text.
        var isOwnProfile = string.Equals(dto.User.UID, UID, StringComparison.Ordinal);
        var description = isOwnProfile ? ProfileJsonPS.GetAboutMe(json) : ProfileJsonPS.ToDisplayText(json);
        return profile with { Description = description };
    }

    public override async Task UserSetProfile(UserProfileDto userDescription)
    {
        // A null description means "leave it as it is" (picture or NSFW changes), so there's nothing to convert
        if (userDescription.Description == null || !IsConnected)
        {
            await base.UserSetProfile(userDescription).ConfigureAwait(false);
            return;
        }

        // If the profile is PlayerSync's JSON, replace only its "about me" text. Sending plain text would erase the
        // name, pronouns, interests, theme etc. set in PlayerSync's own editor.
        var current = await base.UserGetProfile(new UserDto(new UserData(UID))).ConfigureAwait(false);
        if (ProfileJsonPS.TryParse(current.Description) is { } json)
        {
            Logger.LogDebug("[PS] Updating only the about me text of the JSON profile");
            userDescription = userDescription with { Description = ProfileJsonPS.WithAboutMe(json, userDescription.Description) };
        }

        await base.UserSetProfile(userDescription).ConfigureAwait(false);
    }
}
