using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.WebAPI;

namespace LaciSynchroni.SyncConfiguration.Configurations;

[Serializable]
public class ServerConfig : ISyncConfiguration
{
    public int CurrentServer { get; set; } = 0;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {

    };

    public bool SendCensusData { get; set; } = false;
    public bool ShownCensusPopup { get; set; } = false;
    public bool ShowServerPickerInMainMenu { get; set; } = false;
    public bool EnableMultiConnect { get; set; } = true;

    public int Version { get; set; } = 2;
}