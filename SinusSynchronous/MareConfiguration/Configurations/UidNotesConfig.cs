using SinusSynchronous.SinusConfiguration.Models;

namespace SinusSynchronous.SinusConfiguration.Configurations;

public class UidNotesConfig : ISinusConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
