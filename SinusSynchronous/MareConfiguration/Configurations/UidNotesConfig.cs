using SinusSynchronous.MareConfiguration.Models;

namespace SinusSynchronous.MareConfiguration.Configurations;

public class UidNotesConfig : IMareConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
