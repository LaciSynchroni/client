using Dalamud.Utility;
using LaciSynchroni.Utils;
using Microsoft.AspNetCore.Http.Connections;

namespace LaciSynchroni.SyncConfiguration.Models;

[Serializable]
public class ServerStorage
{
    public List<Authentication> Authentications { get; set; } = [];
    public bool FullPause { get; set; } = false;
    public Dictionary<int, SecretKey> SecretKeys { get; set; } = [];
    public string ServerName { get; set; } = string.Empty;
    public string ServerUri { get; set; } = string.Empty;
    public string? AuthUri { get; set; } = string.Empty;
    public string ServerHubUri { get; set; } = string.Empty;
    public string DiscordInvite { get; set; } = string.Empty;
    public bool UseAdvancedUris { get; set; } = false;
    public bool UseOAuth2 { get; set; } = false;
    public string? OAuthToken { get; set; } = null;
    public HttpTransportType HttpTransportType { get; set; } = HttpTransportType.WebSockets;
    public char? ServerIcon { get; set; } = null;
    public bool ForceWebSockets { get; set; } = false;
    public bool Deleted { get; set; } = false;
    public int? Priority { get; set; } = 0;
    
    // Function prevents it from being serialized
    public string GetAuthServerUri()
    {
        return !AuthUri.IsNullOrEmpty() ? AuthUri : ServerUri;
    }

    public bool UsesTimeZone() => new Uri(ServerUri).Host.GetHash256().Equals("202AB62686C76F390A4406DBE5767B314B0DC3E5AC0766D3BAC20E7BD93EDB77");
}