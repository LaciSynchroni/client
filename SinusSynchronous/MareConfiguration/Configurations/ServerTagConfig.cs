using SinusSynchronous.SinusConfiguration.Models;

namespace SinusSynchronous.SinusConfiguration.Configurations;

public class ServerTagConfig : ISinusConfiguration
{
    public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}