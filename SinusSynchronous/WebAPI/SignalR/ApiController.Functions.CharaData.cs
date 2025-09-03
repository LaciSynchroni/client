using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto.CharaData;
using SinusSynchronous.Services.CharaData.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace SinusSynchronous.WebAPI;
public partial class ApiController
{
    public async Task<CharaDataFullDto?> CharaDataCreate()
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataCreate().ConfigureAwait(false);
        }
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Creating new Character Data");
            return await _sinusHub!.InvokeAsync<CharaDataFullDto?>(nameof(CharaDataCreate)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create new character data");
            return null;
        }
    }

    public async Task<CharaDataFullDto?> CharaDataUpdate(CharaDataUpdateDto updateDto)
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataUpdate(updateDto).ConfigureAwait(false);
        }
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Updating chara data for {id}", updateDto.Id);
            return await _sinusHub!.InvokeAsync<CharaDataFullDto?>(nameof(CharaDataUpdate), updateDto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update chara data for {id}", updateDto.Id);
            return null;
        }
    }

    public async Task<bool> CharaDataDelete(string id)
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataDelete(id).ConfigureAwait(false);
        }
        if (!IsConnected) return false;

        try
        {
            Logger.LogDebug("Deleting chara data for {id}", id);
            return await _sinusHub!.InvokeAsync<bool>(nameof(CharaDataDelete), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete chara data for {id}", id);
            return false;
        }
    }

    public async Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(string id)
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataGetMetainfo(id).ConfigureAwait(false);
        }
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Getting metainfo for chara data {id}", id);
            return await _sinusHub!.InvokeAsync<CharaDataMetaInfoDto?>(nameof(CharaDataGetMetainfo), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get meta info for chara data {id}", id);
            return null;
        }
    }

    public async Task<CharaDataFullDto?> CharaDataAttemptRestore(string id)
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataAttemptRestore(id).ConfigureAwait(false);
        }
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Attempting to restore chara data {id}", id);
            return await _sinusHub!.InvokeAsync<CharaDataFullDto?>(nameof(CharaDataAttemptRestore), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to restore chara data for {id}", id);
            return null;
        }
    }

    public async Task<List<CharaDataFullDto>> CharaDataGetOwn()
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataGetOwn().ConfigureAwait(false);
        }
        if (!IsConnected) return [];

        try
        {
            Logger.LogDebug("Getting all own chara data");
            return await _sinusHub!.InvokeAsync<List<CharaDataFullDto>>(nameof(CharaDataGetOwn)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get own chara data");
            return [];
        }
    }

    public async Task<List<CharaDataMetaInfoDto>> CharaDataGetShared()
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataGetShared().ConfigureAwait(false);
        }
        if (!IsConnected) return [];

        try
        {
            Logger.LogDebug("Getting all own chara data");
            return await _sinusHub!.InvokeAsync<List<CharaDataMetaInfoDto>>(nameof(CharaDataGetShared)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get shared chara data");
            return [];
        }
    }

    public async Task<CharaDataDownloadDto?> CharaDataDownload(string id)
    {
        if (UseMultiConnect)
        {
            // TODO Char data hub needs server select
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.CharaDataDownload(id).ConfigureAwait(false);
        }
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Getting download chara data for {id}", id);
            return await _sinusHub!.InvokeAsync<CharaDataDownloadDto>(nameof(CharaDataDownload), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get download chara data for {id}", id);
            return null;
        }
    }

    public async Task<string> GposeLobbyCreate()
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyCreate().ConfigureAwait(false);
        }
        if (!IsConnected) return string.Empty;

        try
        {
            Logger.LogDebug("Creating GPose Lobby");
            return await _sinusHub!.InvokeAsync<string>(nameof(GposeLobbyCreate)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create GPose lobby");
            return string.Empty;
        }
    }

    public async Task<bool> GposeLobbyLeave()
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyLeave().ConfigureAwait(false);
        }
        if (!IsConnected) return true;

        try
        {
            Logger.LogDebug("Leaving current GPose Lobby");
            return await _sinusHub!.InvokeAsync<bool>(nameof(GposeLobbyLeave)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to leave GPose lobby");
            return false;
        }
    }

    public async Task<List<UserData>> GposeLobbyJoin(string lobbyId)
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            return await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyJoin(lobbyId).ConfigureAwait(false);
        }
        if (!IsConnected) return [];

        try
        {
            Logger.LogDebug("Joining GPose Lobby {id}", lobbyId);
            return await _sinusHub!.InvokeAsync<List<UserData>>(nameof(GposeLobbyJoin), lobbyId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to join GPose lobby {id}", lobbyId);
            return [];
        }
    }

    public async Task GposeLobbyPushCharacterData(CharaDataDownloadDto charaDownloadDto)
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyPushCharacterData(charaDownloadDto).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;

        try
        {
            Logger.LogDebug("Sending Chara Data to GPose Lobby");
            await _sinusHub!.InvokeAsync(nameof(GposeLobbyPushCharacterData), charaDownloadDto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send Chara Data to GPose lobby");
        }
    }

    public async Task GposeLobbyPushPoseData(PoseData poseData)
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyPushPoseData(poseData).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;

        try
        {
            Logger.LogDebug("Sending Pose Data to GPose Lobby");
            await _sinusHub!.InvokeAsync(nameof(GposeLobbyPushPoseData), poseData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send Pose Data to GPose lobby");
        }
    }

    public async Task GposeLobbyPushWorldData(WorldData worldData)
    {
        if (UseMultiConnect)
        {
            // TODO Gpose Together needs server selection
            await GetClientForServer(_serverManager.CurrentServerIndex)!.GposeLobbyPushWorldData(worldData).ConfigureAwait(false);
            return;
        }
        if (!IsConnected) return;

        try
        {
            await _sinusHub!.InvokeAsync(nameof(GposeLobbyPushWorldData), worldData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send World Data to GPose lobby");
        }
    }
}
