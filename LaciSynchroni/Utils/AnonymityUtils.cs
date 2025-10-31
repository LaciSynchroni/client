using Dalamud.Utility;

namespace LaciSynchroni.Utils;

public static class AnonymityUtils
{
    public static string ShortenPlayerName(string? name)
    {
        if (name.IsNullOrEmpty())
        {
            return "";
        }

        var parts = name.Split(" ").Select(s => s.Substring(0, 1));
        return String.Join(". ", parts) + ".";
    }
}