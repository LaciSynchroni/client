using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.Common.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LaciSynchroni.WebAPI;

public partial class SyncHubClient
{
    public async Task PushCharacterData(CharacterData data, List<UserData> visibleCharacters)
    {
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

    public virtual async Task UserAddPair(UserDto user)
    {
        if (!IsConnected) return;
        await _connection!.SendAsync(nameof(UserAddPair), user).ConfigureAwait(false);
    }

    public async Task UserDelete()
    {
        CheckConnection();
        await _connection!.SendAsync(nameof(UserDelete)).ConfigureAwait(false);
        await CreateConnectionsAsync().ConfigureAwait(false);
    }

    public async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs(CensusDataDto? censusDataDto)
    {
        return await _connection!.InvokeAsync<List<OnlineUserIdentDto>>(nameof(UserGetOnlinePairs), censusDataDto).ConfigureAwait(false);
    }

    public async Task<List<UserFullPairDto>> UserGetPairedClients()
    {
        return await _connection!.InvokeAsync<List<UserFullPairDto>>(nameof(UserGetPairedClients)).ConfigureAwait(false);
    }

    public async Task<UserProfileDto> UserGetProfile(UserDto dto)
    {
        if (!IsConnected) return new UserProfileDto(dto.User, Disabled: false, IsNSFW: null, ProfilePictureBase64: null, Description: null);
        return await _connection!.InvokeAsync<UserProfileDto>(nameof(UserGetProfile), dto).ConfigureAwait(false);
    }

    public async Task UserPushData(UserCharaDataMessageDto dto)
    {
        try
        {
            await _connection!.InvokeAsync(nameof(UserPushData), dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to Push character data");
        }
    }

    public async Task SetBulkPermissions(BulkPermissionsDto dto)
    {
        CheckConnection();

        try
        {
            await _connection!.InvokeAsync(nameof(SetBulkPermissions), dto).ConfigureAwait(false);
            _logger.LogDebug("Executed SetBulkPermissions {dto}", dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set permissions");
        }
    }

    public async Task UserRemovePair(UserDto userDto)
    {
        if (!IsConnected) return;
        await _connection!.SendAsync(nameof(UserRemovePair), userDto).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(UserPermissionsDto userPermissions)
    {
        await SetBulkPermissions(new(new(StringComparer.Ordinal)
        {
                { userPermissions.User.UID, userPermissions.Permissions }
            }, new(StringComparer.Ordinal))).ConfigureAwait(false);
    }

    public async Task UserSetProfile(UserProfileDto userDescription)
    {
        if (!IsConnected) return;
        await _connection!.InvokeAsync(nameof(UserSetProfile), userDescription).ConfigureAwait(false);
    }

    public async Task UserUpdateDefaultPermissions(DefaultPermissionsDto defaultPermissionsDto)
    {
        CheckConnection();
        await _connection!.InvokeAsync(nameof(UserUpdateDefaultPermissions), defaultPermissionsDto).ConfigureAwait(false);
    }

    private async Task PushCharacterDataInternal(CharacterData character, List<UserData> visibleCharacters)
    {
        Logger.LogInformation("Pushing character data for {hash} to {charas}", character.DataHash.Value, string.Join(", ", visibleCharacters.Select(c => c.AliasOrUID)));
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
        // if (_serverManager.SendCensusData && _lastCensus != null)
        // {
        //     var world = await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(false);
        //     censusDto = new((ushort)world, _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
        //     Logger.LogDebug("Attaching Census Data: {data}", censusDto);
        // }

        await UserPushData(new(visibleCharacters, character, censusDto)).ConfigureAwait(false);
    }
}