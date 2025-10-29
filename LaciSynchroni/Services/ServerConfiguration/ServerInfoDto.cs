namespace LaciSynchroni.Services.ServerConfiguration
{
    public class ServerInfoDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;
        public string HubUri { get; set; } = string.Empty;
        public bool FullPause { get; set; } = false;
    }
}
