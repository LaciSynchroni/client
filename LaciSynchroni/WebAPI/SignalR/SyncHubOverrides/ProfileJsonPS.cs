using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// PlayerSync stores its profile as JSON (their ProfileV1, camelCase keys, enums as numbers, default values omitted)
/// inside <see cref="Common.Dto.User.UserProfileDto.Description"/>. Profiles saved before that change, or by other
/// clients, are plain text. Only used inside <see cref="SyncHubClientPS"/>.
/// </summary>
internal static class ProfileJsonPS
{
    private const string AboutMeKey = "aboutMe";

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    // The default encoder writes every non-ASCII character as \uXXXX, which bloats the profile (6 characters per emoji or
    // CJK character) toward the server's length limit. The JSON never ends up in HTML, so relaxed escaping is safe.
    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Values must match PlayerSync's ProfileStatus enum. NotShared (0) is not displayed.
    /// </summary>
    private static readonly string[] StatusNames =
        ["", "Not interested", "Taken", "Open", "Looking", "It's complicated", "Ask me"];

    /// <summary>
    /// Returns the profile as a JSON object, or null when <paramref name="raw"/> is plain text.
    /// </summary>
    public static JsonObject? TryParse(string? raw)
    {
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith('{')) return null;
        try
        {
            if (JsonNode.Parse(raw, NodeOptions) is not JsonObject profile) return null;
            // JsonObject builds its key lookup lazily and throws on keys that differ only in case, so force it here
            _ = profile.Count;
            return profile;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static string GetAboutMe(JsonObject profile) => GetString(profile, AboutMeKey);

    /// <summary>
    /// The profile as readable text: preferred name, pronouns, status and interests above the "about me" text.
    /// </summary>
    public static string ToDisplayText(JsonObject profile)
    {
        var sb = new StringBuilder();

        var name = GetString(profile, "preferredName");
        var pronouns = GetString(profile, "pronouns");
        if (name.Length > 0 && pronouns.Length > 0) sb.AppendLine($"{name} ({pronouns})");
        else if (name.Length > 0) sb.AppendLine(name);
        else if (pronouns.Length > 0) sb.AppendLine($"Pronouns: {pronouns}");

        if (profile["status"] is JsonValue statusValue && statusValue.TryGetValue<int>(out var status)
            && status > 0 && status < StatusNames.Length)
        {
            sb.AppendLine($"Status: {StatusNames[status]}");
        }

        if (profile["interests"] is JsonArray interestsArray)
        {
            var interests = interestsArray
                .Select(i => i is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : string.Empty)
                .Where(s => s.Length > 0)
                .ToList();
            if (interests.Count > 0) sb.AppendLine($"Interests: {string.Join(", ", interests)}");
        }

        var aboutMe = GetAboutMe(profile);
        if (aboutMe.Length > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(aboutMe);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Replaces only the "about me" text, keeping every other field PlayerSync's own editor set.
    /// </summary>
    public static string WithAboutMe(JsonObject profile, string aboutMe)
    {
        // Remove any differently cased key first so the result has exactly one, in PlayerSync's casing
        foreach (var key in profile.Select(kv => kv.Key).Where(k => string.Equals(k, AboutMeKey, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            profile.Remove(key);
        }

        // PlayerSync omits default values, so an empty "about me" is left out
        if (aboutMe.Length > 0) profile[AboutMeKey] = aboutMe;

        return profile.ToJsonString(WriteOptions);
    }

    private static string GetString(JsonObject profile, string key)
        => profile[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : string.Empty;
}
