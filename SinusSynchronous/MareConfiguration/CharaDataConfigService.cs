using SinusSynchronous.MareConfiguration.Configurations;

namespace SinusSynchronous.MareConfiguration;

public class CharaDataConfigService : ConfigurationServiceBase<CharaDataConfig>
{
    public const string ConfigName = "charadata.json";

    public CharaDataConfigService(string configDir) : base(configDir) { }
    public override string ConfigurationName => ConfigName;
}