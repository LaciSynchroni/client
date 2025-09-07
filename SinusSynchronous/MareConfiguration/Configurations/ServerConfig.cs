using SinusSynchronous.SinusConfiguration.Models;
using SinusSynchronous.WebAPI;

namespace SinusSynchronous.SinusConfiguration.Configurations;

[Serializable]
public class ServerConfig : ISinusConfiguration
{
    public int CurrentServer { get; set; } = 0;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {
        { new ServerStorage() { ServerName = ApiController.MainServer, ServerUri = ApiController.MainServiceUri, UseOAuth2 = true } },
    };

    public bool SendCensusData { get; set; } = false;
    public bool ShownCensusPopup { get; set; } = false;
    public bool ShowServerPickerInMainMenu { get; set; } = false;
    public bool EnableMultiConnect { get; set; } = true;

    public int Version { get; set; } = 2;
}