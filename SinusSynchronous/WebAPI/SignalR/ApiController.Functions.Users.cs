using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto;
using SinusSynchronous.API.Dto.User;

namespace SinusSynchronous.WebAPI;
using ServerIndex = int;

#pragma warning disable MA0040
public partial class ApiController
{
    
    public async Task PushCharacterData(ServerIndex serverIndex, CharacterData data, List<UserData> visibleCharacters)
    {
        await GetClientForServer(serverIndex)!.PushCharacterData(data, visibleCharacters).ConfigureAwait(false);
    }

    public Task UserAddPairToServer(ServerIndex serverIndex, string pairToAdd)
    {
        return UserAddPair(serverIndex, new(new(pairToAdd)));
    }

    public async Task UserAddPair(ServerIndex serverIndex, UserDto user)
    {
        await GetClientForServer(serverIndex)!.UserAddPair(user).ConfigureAwait(false);
    }

    public async Task UserDelete(ServerIndex serverIndex)
    {
        await GetClientForServer(serverIndex)!.UserDelete().ConfigureAwait(false);
    }

    public async Task<UserProfileDto> UserGetProfile(ServerIndex serverIndex, UserDto dto)
    {
        return await GetClientForServer(serverIndex)!.UserGetProfile(dto).ConfigureAwait(false);
    }

    public async Task SetBulkPermissions(ServerIndex serverIndex, BulkPermissionsDto dto)
    {
        await GetClientForServer(serverIndex)!.SetBulkPermissions(dto).ConfigureAwait(false);
    }

    public async Task UserRemovePair(ServerIndex serverIndex, UserDto userDto)
    {
        await GetClientForServer(serverIndex)!.UserRemovePair(userDto).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(ServerIndex serverIndex, UserPermissionsDto userPermissions)
    {
        await GetClientForServer(serverIndex)!.UserSetPairPermissions(userPermissions).ConfigureAwait(false);
    }

    public async Task UserSetProfile(ServerIndex serverIndex, UserProfileDto userDescription)
    {
        await GetClientForServer(serverIndex)!.UserSetProfile(userDescription).ConfigureAwait(false);
    }

    public async Task UserUpdateDefaultPermissions(ServerIndex serverIndex, DefaultPermissionsDto defaultPermissionsDto)
    {
        await GetClientForServer(serverIndex)!.UserUpdateDefaultPermissions(defaultPermissionsDto).ConfigureAwait(false);
    }
}
#pragma warning restore MA0040