using LaciSynchroni.Common.Dto.User;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;
internal class SyncHubClientPS : SyncHubClient
{
    public SyncHubClientPS(int serverIndex,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager,
        DalamudUtilService dalamudUtilService,
        ILoggerFactory loggerFactory, ILoggerProvider loggerProvider, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) :
        base(serverIndex, serverConfigurationManager, pairManager, dalamudUtilService, loggerFactory, loggerProvider, mediator, multiConnectTokenService, syncConfigService, httpClient)
    {

    }

    public override async Task UserAddPair(UserDto user)
    {
        if (!IsConnected) return;
        // Add an extra parameter for PS to tell it this isn't part of their pair request system
        await _connection!.SendAsync(nameof(UserAddPair), user, false).ConfigureAwait(false);
    }
}
