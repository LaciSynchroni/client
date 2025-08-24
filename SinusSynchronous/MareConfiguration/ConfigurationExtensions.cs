using SinusSynchronous.MareConfiguration.Configurations;

namespace SinusSynchronous.MareConfiguration;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this MareConfig configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder);
    }
}