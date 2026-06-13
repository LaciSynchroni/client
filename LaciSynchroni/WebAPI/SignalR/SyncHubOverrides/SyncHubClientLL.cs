using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;
internal class SyncHubClientLL : SyncHubClient
{
    public SyncHubClientLL(int serverIndex,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager,
        DalamudUtilService dalamudUtilService,
        ILoggerFactory loggerFactory, ILoggerProvider loggerProvider, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) :
        base(serverIndex, serverConfigurationManager, pairManager, dalamudUtilService, loggerFactory, loggerProvider, mediator, multiConnectTokenService, syncConfigService, httpClient, true)
    {
        if (syncConfigService.Current.IsAllowedToConnectBlake3())
        {
            // Blake3 is required to access API 37
            ApiVersion = 37;
        }
        else
        {
            // Keep on 36 - first connect will prompt blake3 migration, which will trigger a re-create and then a bump to 37
            // LEAVE THIS VERSION - even if API bumps past 37. Allows us to easily control connect.
            ApiVersion = 36;
        }
    }
}
