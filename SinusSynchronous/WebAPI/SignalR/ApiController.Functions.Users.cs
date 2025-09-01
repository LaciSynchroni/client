using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto;
using SinusSynchronous.API.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text;

namespace SinusSynchronous.WebAPI;

#pragma warning disable MA0040
public partial class ApiController
{
    public async Task PushCharacterData(int serverIndex, CharacterData data, List<UserData> visibleCharacters)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.PushCharacterData(data, visibleCharacters).ConfigureAwait(false);
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

    public async Task UserAddPair(UserDto user)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserAddPair(user).ConfigureAwait(false);
            return;
        }

        if (!IsConnected) return;
        await _sinusHub!.SendAsync(nameof(UserAddPair), user).ConfigureAwait(false);
    }

    public async Task UserDelete()
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserDelete().ConfigureAwait(false);
            return;
        }

        CheckConnection();
        await _sinusHub!.SendAsync(nameof(UserDelete)).ConfigureAwait(false);
        await CreateConnectionsAsync().ConfigureAwait(false);
    }

    public async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs(CensusDataDto? censusDataDto)
    {
        return await _sinusHub!.InvokeAsync<List<OnlineUserIdentDto>>(nameof(UserGetOnlinePairs), censusDataDto)
            .ConfigureAwait(false);
    }

    public async Task<List<UserFullPairDto>> UserGetPairedClients()
    {
        return await _sinusHub!.InvokeAsync<List<UserFullPairDto>>(nameof(UserGetPairedClients)).ConfigureAwait(false);
    }

    public async Task<UserProfileDto> UserGetProfile(UserDto dto)
    {
        if (_useMultiConnect)
        {
            return await _currentSinusClient!.UserGetProfile(dto).ConfigureAwait(false);
        }

        if (!IsConnected)
            return new UserProfileDto(dto.User, Disabled: false, IsNSFW: null, ProfilePictureBase64: null,
                Description: null);
        return await _sinusHub!.InvokeAsync<UserProfileDto>(nameof(UserGetProfile), dto).ConfigureAwait(false);
    }

    public async Task UserPushData(UserCharaDataMessageDto dto)
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

    public async Task SetBulkPermissions(BulkPermissionsDto dto)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.SetBulkPermissions(dto).ConfigureAwait(false);
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

    public async Task UserRemovePair(UserDto userDto)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserRemovePair(userDto).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;
        await _sinusHub!.SendAsync(nameof(UserRemovePair), userDto).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(UserPermissionsDto userPermissions)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserSetPairPermissions(userPermissions).ConfigureAwait(false);
            return;
        }
        await SetBulkPermissions(new(
            new(StringComparer.Ordinal) { { userPermissions.User.UID, userPermissions.Permissions } },
            new(StringComparer.Ordinal))).ConfigureAwait(false);
    }

    public async Task UserSetProfile(UserProfileDto userDescription)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserSetProfile(userDescription).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;
        await _sinusHub!.InvokeAsync(nameof(UserSetProfile), userDescription).ConfigureAwait(false);
    }

    public async Task UserUpdateDefaultPermissions(DefaultPermissionsDto defaultPermissionsDto)
    {
        if (_useMultiConnect)
        {
            await _currentSinusClient!.UserUpdateDefaultPermissions(defaultPermissionsDto).ConfigureAwait(false);
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