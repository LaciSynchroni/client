using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;
internal class SyncHubClientLL : SyncHubClient
{
    public SyncHubClientLL(int serverIndex,
        ServerConfigurationManager serverConfigurationManager, PairManager pairManager,
        DalamudUtilService dalamudUtilService,
        ILoggerFactory loggerFactory, ILoggerProvider loggerProvider, SyncMediator mediator, MultiConnectTokenService multiConnectTokenService, SyncConfigService syncConfigService, HttpClient httpClient) :
        base(serverIndex, serverConfigurationManager, pairManager, dalamudUtilService, loggerFactory, loggerProvider, mediator, multiConnectTokenService, syncConfigService, httpClient)
    {
        if (syncConfigService.Current.IsAllowedToConnectBlake3())
        {
            // Blake3 is required to access API 37
            ApiVersion = 37;
        }
        else
        {
            var serverName = serverConfigurationManager.GetServerNameByIndex(serverIndex);
            mediator.Publish(new NotificationMessage("BLAKE3 Support Disabled",
                $"BLAKE3 support is needed to connect to {serverName}. Please go to Settings -> Debug and enable BLAKE3 Support to connect to this service.",
                NotificationType.Error));
            ApiVersion = 36;
        }
    }
}
