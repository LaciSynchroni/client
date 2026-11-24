using LaciSynchroni.Common.Data;
using LaciSynchroni.Common.Dto.User;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;
internal partial class SyncHubClientPS : SyncHubClient
{
    public SyncHubClientPS(int serverIndex,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager,
        DalamudUtilService dalamudUtilService,
        ILoggerFactory loggerFactory, ILoggerProvider loggerProvider, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) :
        base(serverIndex, serverConfigurationManager, pairManager, dalamudUtilService, loggerFactory, loggerProvider, mediator, multiConnectTokenService, syncConfigService, httpClient, false)
    {
        ApiVersion = 40;
        _pairManagerPS = pairManager;
        _serverConfigurationManagerPS = serverConfigurationManager;
        SubscribeToRequestActions();
    }

    public override async Task UserAddPair(UserDto user)
    {
        if (!IsConnected) return;
        // Add an extra parameter for PS to tell it this isn't part of their pair request system
        await _connection!.SendAsync(nameof(UserAddPair), user, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Last full character data received per UID on this connection. PlayerSync sends partial updates
    /// (only one addon's data in an otherwise empty <see cref="CharacterData"/>), which get merged into this
    /// so the rest of Laci only ever sees full character data.
    /// </summary>
    private readonly ConcurrentDictionary<string, CharacterData> _lastFullCharacterData = new(StringComparer.Ordinal);

    protected override void RegisterUserReceiveCharacterData()
    {
        // Called once per new HubConnection, not on SignalR's automatic reconnects (those reuse the connection
        // and its handlers). Data from a previous connection must not be merged into. Entries kept across an
        // automatic reconnect are corrected by the next full data the pair sends.
        _lastFullCharacterData.Clear();

        _connection!.On<PsOnlineUserCharaDataDto>(nameof(Client_UserReceiveCharacterData), dto => _ = ReceivePsCharacterData(dto));
        // In addition to the base handler, forget the data of users that go offline
        _connection!.On<UserDto>(nameof(Client_UserSendOffline), dto => _lastFullCharacterData.TryRemove(dto.User.UID, out _));

        RegisterRequestHandlers();
    }

    private Task ReceivePsCharacterData(PsOnlineUserCharaDataDto dto)
    {
        try
        {
            var uid = dto.User.UID;
            var data = dto.CharaData;
            Logger.LogDebug("[PS] Character data for {uid}: AddonPlugin: {plugin}, Files: {files}, HasGlamourer: {glamourer}",
                uid, dto.AddonPlugin?.ToString() ?? "none (full data)", data.FileReplacements.Sum(k => k.Value.Count), data.GlamourerData.Count > 0);

            if (dto.AddonPlugin is not { } plugin)
            {
                _lastFullCharacterData[uid] = data.DeepClone();
                return Client_UserReceiveCharacterData(new OnlineUserCharaDataDto(dto.User, data));
            }

            if (!_lastFullCharacterData.TryGetValue(uid, out var lastFull))
            {
                Logger.LogDebug("[PS] Dropping partial {plugin} data for {uid}: no full data received yet", plugin, uid);
                return Task.CompletedTask;
            }

            var merged = lastFull.DeepClone();
            switch (plugin)
            {
                case PsAddonPlugin.Heels: merged.HeelsData = data.HeelsData; break;
                case PsAddonPlugin.Honorific: merged.HonorificData = data.HonorificData; break;
                case PsAddonPlugin.Moodles: merged.MoodlesData = data.MoodlesData; break;
                case PsAddonPlugin.PetNames: merged.PetNamesData = data.PetNamesData; break;
                default:
                    // e.g. Loci, which Laci doesn't support
                    Logger.LogDebug("[PS] Dropping partial data for unsupported addon {plugin} for {uid}", plugin, uid);
                    return Task.CompletedTask;
            }

            Logger.LogDebug("[PS] Merged partial {plugin} data into last full data for {uid}", plugin, uid);
            _lastFullCharacterData[uid] = merged;
            return Client_UserReceiveCharacterData(new OnlineUserCharaDataDto(dto.User, merged.DeepClone()));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[PS] Failed to process character data for {uid}", dto.User.UID);
            return Task.CompletedTask;
        }
    }
}
