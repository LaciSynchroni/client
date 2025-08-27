using SinusSynchronous.SinusConfiguration.Configurations;

namespace SinusSynchronous.SinusConfiguration;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this SinusConfig configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder);
    }
}