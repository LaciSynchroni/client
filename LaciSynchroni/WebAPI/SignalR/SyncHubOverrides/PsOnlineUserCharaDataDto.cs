using LaciSynchroni.Common.Data;
using MessagePack;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// PlayerSync's variant of <see cref="Common.Dto.User.OnlineUserCharaDataDto"/>.
/// When <see cref="AddonPlugin"/> is set, <see cref="CharaData"/> is a partial update that only carries that addon's data
/// in an otherwise empty <see cref="CharacterData"/>. Only used inside <see cref="SyncHubClientPS"/>.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record PsOnlineUserCharaDataDto(UserData User, CharacterData CharaData, PsAddonPlugin? AddonPlugin = null);

/// <summary>
/// Values must match PlayerSync's API (MareSynchronos.API.Data.Enum.AddonPlugin), as the nullable enum is sent as its number.
/// </summary>
public enum PsAddonPlugin
{
    Heels = 5,
    Honorific = 7,
    Moodles = 9,
    PetNames = 10,
    Loci = 11,
}
