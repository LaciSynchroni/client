using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto;
using SinusSynchronous.API.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text;

namespace SinusSynchronous.WebAPI;
using ServerIndex = int;

#pragma warning disable MA0040
public partial class ApiController
{
    
    public async Task PushCharacterData(ServerIndex serverIndex, CharacterData data, List<UserData> visibleCharacters)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.PushCharacterData(data, visibleCharacters).ConfigureAwait(false);
            return;
        }

        if (!IsConnected) return;

        try
        {
            await PushCharacterDataInternal(data, [.. visibleCharacters]).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Upload operation was cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during upload of files");
        }
    }

    public Task UserAddPairToServer(ServerIndex serverIndex, string pairToAdd)
    {
        return UserAddPair(serverIndex, new(new(pairToAdd)));
    }

    public async Task UserAddPair(ServerIndex serverIndex, UserDto user)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserAddPair(user).ConfigureAwait(false);
            return;
        }

        if (!IsConnected) return;
        await _sinusHub!.SendAsync(nameof(UserAddPair), user).ConfigureAwait(false);
    }

    public async Task UserDelete(ServerIndex serverIndex)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserDelete().ConfigureAwait(false);
            return;
        }

        CheckConnection();
        await _sinusHub!.SendAsync(nameof(UserDelete)).ConfigureAwait(false);
        await CreateConnectionsAsync(serverIndex).ConfigureAwait(false);
    }

    private async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs(CensusDataDto? censusDataDto)
    {
        return await _sinusHub!.InvokeAsync<List<OnlineUserIdentDto>>(nameof(UserGetOnlinePairs), censusDataDto)
            .ConfigureAwait(false);
    }

    private async Task<List<UserFullPairDto>> UserGetPairedClients()
    {
        if (UseMultiConnect)
        {
            throw new InvalidOperationException("Not supported for _multiConnect, please call multi connect sinus client instead!");
        }
        return await _sinusHub!.InvokeAsync<List<UserFullPairDto>>(nameof(UserGetPairedClients)).ConfigureAwait(false);
    }

    public async Task<UserProfileDto> UserGetProfile(ServerIndex serverIndex, UserDto dto)
    {
        if (UseMultiConnect)
        {
            return await GetClientForServer(serverIndex)!.UserGetProfile(dto).ConfigureAwait(false);
        }

        if (!IsConnected)
            return new UserProfileDto(dto.User, Disabled: false, IsNSFW: null, ProfilePictureBase64: null,
                Description: null);
        return await _sinusHub!.InvokeAsync<UserProfileDto>(nameof(UserGetProfile), dto).ConfigureAwait(false);
    }

    private async Task UserPushData(UserCharaDataMessageDto dto)
    {
        try
        {
            await _sinusHub!.InvokeAsync(nameof(UserPushData), dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to Push character data");
        }
    }

    public async Task SetBulkPermissions(ServerIndex serverIndex, BulkPermissionsDto dto)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.SetBulkPermissions(dto).ConfigureAwait(false);
            return;
        }

        CheckConnection();

        try
        {
            await _sinusHub!.InvokeAsync(nameof(SetBulkPermissions), dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to set permissions");
        }
    }

    public async Task UserRemovePair(ServerIndex serverIndex, UserDto userDto)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserRemovePair(userDto).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;
        await _sinusHub!.SendAsync(nameof(UserRemovePair), userDto).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(ServerIndex serverIndex, UserPermissionsDto userPermissions)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserSetPairPermissions(userPermissions).ConfigureAwait(false);
            return;
        }
        await SetBulkPermissions(serverIndex, new(
            new(StringComparer.Ordinal) { { userPermissions.User.UID, userPermissions.Permissions } },
            new(StringComparer.Ordinal))).ConfigureAwait(false);
    }

    public async Task UserSetProfile(ServerIndex serverIndex, UserProfileDto userDescription)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserSetProfile(userDescription).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;
        await _sinusHub!.InvokeAsync(nameof(UserSetProfile), userDescription).ConfigureAwait(false);
    }

    public async Task UserUpdateDefaultPermissions(ServerIndex serverIndex, DefaultPermissionsDto defaultPermissionsDto)
    {
        if (UseMultiConnect)
        {
            await GetClientForServer(serverIndex)!.UserUpdateDefaultPermissions(defaultPermissionsDto).ConfigureAwait(false);
            return;
        }
        CheckConnection();
        await _sinusHub!.InvokeAsync(nameof(UserUpdateDefaultPermissions), defaultPermissionsDto).ConfigureAwait(false);
    }

    private async Task PushCharacterDataInternal(CharacterData character, List<UserData> visibleCharacters)
    {
        Logger.LogInformation("Pushing character data for {hash} to {charas}", character.DataHash.Value,
            string.Join(", ", visibleCharacters.Select(c => c.AliasOrUID)));
        StringBuilder sb = new();
        foreach (var kvp in character.FileReplacements.ToList())
        {
            sb.AppendLine($"FileReplacements for {kvp.Key}: {kvp.Value.Count}");
        }

        foreach (var item in character.GlamourerData)
        {
            sb.AppendLine($"GlamourerData for {item.Key}: {!string.IsNullOrEmpty(item.Value)}");
        }

        Logger.LogDebug("Chara data contained: {nl} {data}", Environment.NewLine, sb.ToString());

        CensusDataDto? censusDto = null;
        if (_serverManager.SendCensusData && _lastCensus != null)
        {
            var world = await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(false);
            censusDto = new((ushort)world, _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
            Logger.LogDebug("Attaching Census Data: {data}", censusDto);
        }

        await UserPushData(new(visibleCharacters, character, censusDto)).ConfigureAwait(false);
    }
}
#pragma warning restore MA0040