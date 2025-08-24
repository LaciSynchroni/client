using SinusSynchronous.MareConfiguration.Configurations;

namespace SinusSynchronous.MareConfiguration;

public class MareConfigService : ConfigurationServiceBase<MareConfig>
{
    public const string ConfigName = "config.json";

    public MareConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}