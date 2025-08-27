using SinusSynchronous.SinusConfiguration.Configurations;

namespace SinusSynchronous.SinusConfiguration;

public class SinusConfigService : ConfigurationServiceBase<SinusConfig>
{
    public const string ConfigName = "config.json";

    public SinusConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}