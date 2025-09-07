using SinusSynchronous.API.Data;
using SinusSynchronous.API.Dto.CharaData;

namespace SinusSynchronous.WebAPI;

using ServerIndex = int;

public partial class ApiController
{
    public async Task<CharaDataFullDto?> CharaDataCreate(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.CharaDataCreate().ConfigureAwait(false);
    }

    public async Task<CharaDataFullDto?> CharaDataUpdate(ServerIndex serverIndex, CharaDataUpdateDto updateDto)
    {
        return await GetClientForServer(serverIndex)!.CharaDataUpdate(updateDto).ConfigureAwait(false);
    }

    public async Task<bool> CharaDataDelete(ServerIndex serverIndex, string id)
    {
        return await GetClientForServer(serverIndex)!.CharaDataDelete(id).ConfigureAwait(false);
    }

    public async Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(ServerIndex serverIndex, string id)
    {
        return await GetClientForServer(serverIndex)!.CharaDataGetMetainfo(id).ConfigureAwait(false);
    }

    public async Task<CharaDataFullDto?> CharaDataAttemptRestore(ServerIndex serverIndex, string id)
    {
        return await GetClientForServer(serverIndex)!.CharaDataAttemptRestore(id).ConfigureAwait(false);
    }

    public async Task<List<CharaDataFullDto>> CharaDataGetOwn(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.CharaDataGetOwn().ConfigureAwait(false);
    }

    public async Task<List<CharaDataMetaInfoDto>> CharaDataGetShared(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.CharaDataGetShared().ConfigureAwait(false);
    }

    public async Task<CharaDataDownloadDto?> CharaDataDownload(ServerIndex serverIndex, string id)
    {
        return await GetClientForServer(serverIndex)!.CharaDataDownload(id).ConfigureAwait(false);
    }

    public async Task<string> GposeLobbyCreate(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.GposeLobbyCreate().ConfigureAwait(false);
    }

    public async Task<bool> GposeLobbyLeave(ServerIndex serverIndex)
    {
        return await GetClientForServer(serverIndex)!.GposeLobbyLeave().ConfigureAwait(false);
    }

    public async Task<List<UserData>> GposeLobbyJoin(ServerIndex serverIndex, string lobbyId)
    {
        return await GetClientForServer(serverIndex)!.GposeLobbyJoin(lobbyId).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushCharacterData(ServerIndex serverIndex, CharaDataDownloadDto charaDownloadDto)
    {
        await GetClientForServer(serverIndex)!.GposeLobbyPushCharacterData(charaDownloadDto).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushPoseData(ServerIndex serverIndex, PoseData poseData)
    {
        await GetClientForServer(serverIndex)!.GposeLobbyPushPoseData(poseData).ConfigureAwait(false);
    }

    public async Task GposeLobbyPushWorldData(ServerIndex serverIndex, WorldData worldData)
    {
        await GetClientForServer(serverIndex)!.GposeLobbyPushWorldData(worldData).ConfigureAwait(false);
    }
}
