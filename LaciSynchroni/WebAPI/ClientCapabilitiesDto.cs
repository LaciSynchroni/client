using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace LaciSynchroni.WebAPI;

// All fields MUST have a default. This is sent by the client in the header.
// Old clients will NOT send all fields. Third-party clients will NOT send all fields.
// Use a reasonable default for each.
public record ClientCapabilitiesDto(
    string ClientVersion = "unknown",

    // Whether or not "delta updates" are supported. Specifically, whether the client
    // supports the PairReceiveVisualSingle, PairReceiveVisualDelta, and PairReceiveModDelta methods.
    bool DeltaUpdates = false
)
{
    public const string ClientCapabilitiesHeader = "X-Client-Capabilities";

    public string ToHeaderValue()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this);
        return Convert.ToBase64String(json);
    }

    public static ClientCapabilitiesDto GetDefault()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        var versionString = string.Create(CultureInfo.InvariantCulture, $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}");
        return new ClientCapabilitiesDto(versionString);
    }
}