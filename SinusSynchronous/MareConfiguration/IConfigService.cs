using SinusSynchronous.SinusConfiguration.Configurations;

namespace SinusSynchronous.SinusConfiguration;

public interface IConfigService<out T> : IDisposable where T : ISinusConfiguration
{
    T Current { get; }
    string ConfigurationName { get; }
    string ConfigurationPath { get; }
    public event EventHandler? ConfigSave;
    void UpdateLastWriteTime();
}
