using Dalamud.Utility;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;

namespace LaciSynchroni.WebAPI;

public partial class SyncHubClient
{
    private async Task<bool> VerifyCensus()
    {
        if (!_serverConfigurationManager.ShownCensusPopup)
        {
            // Mediator.Publish(new OpenCensusPopupMessage());
            // while (!_serverConfigurationManager.ShownCensusPopup)
            // {
            //     await Task.Delay(500).ConfigureAwait(false);
            // }

            // Set default to not upload census data and don't show the popup again
            _serverConfigurationManager.SendCensusData = false;
            _serverConfigurationManager.ShownCensusPopup = true;

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
        var oauth2 = _serverConfigurationManager.GetOAuth2(out var multi, ServerIndex);
        if (multi)
        {
            Logger.LogWarning("Multiple secret keys for current character");
            ConnectionDto = null;
            Mediator.Publish(new NotificationMessage(
                "Multiple Identical Characters detected",
                "Your Service configuration has multiple characters with the same name and world set up."
                + $" Delete the duplicates in the character management to be able to connect to a {_dalamudUtil.GetPluginName()} server.",
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

        if (!await _multiConnectTokenService.TryUpdateOAuth2LoginTokenAsync(ServerIndex, _serverConfigurationManager.GetServerByIndex(ServerIndex)).ConfigureAwait(false))
        {
            Logger.LogWarning("OAuth2 login token could not be updated");
            ConnectionDto = null;
            await StopConnectionAsync(ServerState.OAuthLoginTokenStale).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return false;
        }

        return true;
    }

    private async Task<bool> VerifyClientVersion(ConnectionDto connectionDto)
    {
        if (ServerToUse.BypassVersionCheck)
        {
            return true;
        }
        var currentClientVer = Assembly.GetExecutingAssembly().GetName().Version!;
        if (connectionDto.ServerVersion != IServerHub.ApiVersion)
        {
            if (connectionDto.CurrentClientVersion > currentClientVer)
            {
                var pluginVersion = string.Create(CultureInfo.InvariantCulture, $"{currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}.{currentClientVer.Revision}");
                var currentClientVersion = string.Create(CultureInfo.InvariantCulture, $"{connectionDto.CurrentClientVersion.Major}.{connectionDto.CurrentClientVersion.Minor}.{connectionDto.CurrentClientVersion.Build}.{connectionDto.CurrentClientVersion.Revision}");
                Mediator.Publish(new NotificationMessage("Client incompatible",
                    $"Your client is outdated ({pluginVersion}), current is: {currentClientVersion}. " +
                    $"This client version is incompatible and will not be able to connect. Please update {_dalamudUtil.GetPluginName()}.",
                    NotificationType.Error));
            }
            return false;
        }
        return true;
    }

    private void TriggerConnectionWarnings(ConnectionDto connectionDto)
    {
        var currentClientVer = Assembly.GetExecutingAssembly().GetName().Version!;

        if (connectionDto.CurrentClientVersion > currentClientVer)
        {
            // Can't really check for version anymore, since some servers have their own plugin
            var pluginVersion = string.Create(CultureInfo.InvariantCulture, $"{currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}.{currentClientVer.Revision}");
            var currentClientVersion = string.Create(CultureInfo.InvariantCulture, $"{connectionDto.CurrentClientVersion.Major}.{connectionDto.CurrentClientVersion.Minor}.{connectionDto.CurrentClientVersion.Build}.{connectionDto.CurrentClientVersion.Revision}");
            Logger.LogWarning("Client outed for {ServerName}. Expected version is {PluginVersion}, current version is {CurrentVersion}", ServerToUse.ServerName, pluginVersion, currentClientVersion);
        }

        if (_dalamudUtil.HasModifiedGameFiles)
        {
            Logger.LogError("Detected modified game files on connection");
            if (!_syncConfigService.Current.DebugStopWhining)
                Mediator.Publish(new NotificationMessage("Modified Game Files detected",
                    "Dalamud is reporting your FFXIV installation has modified game files. Any mods installed through TexTools will produce this message. " +
                    $"{_dalamudUtil.GetPluginName()}, Penumbra, and some other plugins assume your FFXIV installation is unmodified in order to work. " +
                    "Synchronization with pairs/shells can break because of this. Exit the game, open XIVLauncher, click the arrow next to Log In " +
                    "and select 'repair game files' to resolve this issue. Afterwards, do not install any mods with TexTools. Your plugin configurations will remain, as will mods enabled in Penumbra.",
                    NotificationType.Error, TimeSpan.FromSeconds(15)));
        }

        if (_dalamudUtil.IsLodEnabled && !_naggedAboutLod)
        {
            _naggedAboutLod = true;
            Logger.LogWarning("Model LOD is enabled during connection");
            if (!_syncConfigService.Current.DebugStopWhining)
            {
                Mediator.Publish(new NotificationMessage("Model LOD is enabled",
                    "You have \"Use low-detail models on distant objects (LOD)\" enabled." +
                    " Having model LOD enabled is known to be a reason to cause random crashes when loading in or rendering modded pairs." +
                    $" Disabling LOD has a very low performance impact. Disable LOD while using {_dalamudUtil.GetPluginName()}:" +
                    " Go to XIV Menu -> System Configuration -> Graphics Settings and disable the model LOD option.",
                    NotificationType.Warning, TimeSpan.FromSeconds(15)));
            }
        }

        if (_naggedAboutLod && !_dalamudUtil.IsLodEnabled)
        {
            _naggedAboutLod = false;
        }
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
            Mediator.Publish(new NotificationMessage("Multiple Identical Characters detected",
                $"Your Service configuration has multiple characters with the same name and world set up." +
                $" Delete the duplicates in the character management to be able to connect to a {_dalamudUtil.GetPluginName()} server.",
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