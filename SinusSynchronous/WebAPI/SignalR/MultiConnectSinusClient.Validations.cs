using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.SinusConfiguration.Models;
using SinusSynchronous.WebAPI.SignalR.Utils;

namespace SinusSynchronous.WebAPI;

public partial class MultiConnectSinusClient
{

    private async Task<bool> VerifyCensus()
    {
        if (!_serverConfigurationManager.ShownCensusPopup)
        {
            Mediator.Publish(new OpenCensusPopupMessage());
            while (!_serverConfigurationManager.ShownCensusPopup)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }

            return false;
        }

        return true;
    }
    
    private async Task<bool> VerifyFullPause()
    {
        if (ServerToUse?.FullPause ?? true)
        {
            Logger.LogInformation("Not recreating Connection, paused");
            ConnectionDto = null;
            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        return true;
    }
    
    private async Task<bool> VerifyOAuth()
    {
        if (!ServerToUse.UseOAuth2)
        {
            return true;
        }
        var oauth2 = _serverConfigurationManager.GetOAuth2(out bool multi);
        if (multi)
        {
            Logger.LogWarning("Multiple secret keys for current character");
            ConnectionDto = null;
            Mediator.Publish(new NotificationMessage("Multiple Identical Characters detected", "Your Service configuration has multiple characters with the same name and world set up. Delete the duplicates in the character management to be able to connect to Sinus.",
                NotificationType.Error));
            await StopConnectionAsync(ServerState.MultiChara).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        if (!oauth2.HasValue)
        {
            Logger.LogWarning("No UID/OAuth set for current character");
            ConnectionDto = null;
            await StopConnectionAsync(ServerState.OAuthMisconfigured).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        if (!await _multiConnectTokenService.TryUpdateOAuth2LoginTokenAsync(ServerIndex, _serverConfigurationManager.CurrentServer).ConfigureAwait(false))
        {
            Logger.LogWarning("OAuth2 login token could not be updated");
            ConnectionDto = null;
            await StopConnectionAsync(ServerState.OAuthLoginTokenStale).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        return true;
    }
    
    private async Task<bool> VerifySecretKeyAuth()
    {
        if (ServerToUse.UseOAuth2)
        {
            // We're using oAuth, no need to verify secret keys
            return true;
        }
        var secretKey = _serverConfigurationManager.GetSecretKey(out bool multi, ServerIndex);
        if (multi)
        {
            _logger.LogWarning("Multiple secret keys for current character");
            ConnectionDto = null;
            Mediator.Publish(new NotificationMessage("Multiple Identical Characters detected", "Your Service configuration has multiple characters with the same name and world set up. Delete the duplicates in the character management to be able to connect to Sinus.",
                NotificationType.Error));
            await StopConnectionAsync(ServerState.MultiChara).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        if (secretKey.IsNullOrEmpty())
        {
            Logger.LogWarning("No secret key set for current character");
            ConnectionDto = null;
            await StopConnectionAsync(ServerState.NoSecretKey).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        // Checks passed, all good, let's continue!
        return true;
    }
}