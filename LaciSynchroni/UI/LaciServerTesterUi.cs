using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using K4os.Compression.LZ4.Legacy;
using LaciSynchroni.Common.Dto;
using LaciSynchroni.Common.Dto.Files;
using LaciSynchroni.Common.Routes;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Utils;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;

namespace LaciSynchroni.UI;

public sealed class LaciServerTesterUi : WindowMediatorSubscriberBase
{
    private readonly string _goatImagePath;
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;
    private readonly UiSharedService _uiSharedService;

    // Config for the test
    private string _host = "example.com";
    private int _port = 443;
    private string _hubPath = "hub";
    private string _secretKey = "";
    
    // Test status
    private bool _testing;
    private TestStep _testChain;
    private CheckStatus _statusFinal = CheckStatus.NotExecuted;

    // Test context that is used through the entire testing process
    private CancellationTokenSource _cancellationTokenSource = new();
    private string _goatFileHash = "";
    private List<string> _testLog = new();
    private string _accessToken = "";
    private HubConnection? _hubConnection;
    private ConnectionDto? _connectionDto;
    
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;


    public LaciServerTesterUi(
        DalamudUtilService dalamudUtilService, 
        IDalamudPluginInterface pluginInterface,
        ILogger<LaciServerTesterUi> logger,
        PerformanceCollectorService performanceCollectorService,
        UiSharedService uiShared, 
        HttpClient httpClient,
        SyncMediator mediator)
        : base(logger, mediator, $"{dalamudUtilService.GetPluginName()} Server Tester", performanceCollectorService)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330), MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _goatImagePath = Path.Combine(pluginInterface.AssemblyLocation.Directory!.FullName, "images/goat.png");
        _log = logger;
        _httpClient = httpClient;
        _uiSharedService = uiShared;
        _testChain = GetTestChains();
    }

    protected override void Dispose(bool disposing)
    {
        _cancellationTokenSource.Cancel();
        _ = _hubConnection?.DisposeAsync();
    }

    private void DrawUi()
    {
        if (_testing)
        {
            DrawTestStatus();

            if (ImGui.Button(_statusFinal == CheckStatus.Success ? "Reset" : "Abort Test"))
            {
                _ = ResetTestStatus();
                _testing = false;
            }
        }
        else
        {
            DrawInputs();
            if (ImGui.Button("Run Test"))
            {
                _testing = true;
                _ = RunTest();
            }
        }
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Clipboard, "Copy test results to clipboard"))
        {
            ImGui.SetClipboardText(string.Join('\n', _testLog));
        }
        ImGui.Spacing();
        DrawTestLog();
    }

    protected override void DrawInternal()
    {
        DrawUi();
    }

    private async Task RunTest()
    {
        _testLog = new List<string>();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        
        
        var testSuccess = await _testChain.Execute().ConfigureAwait(false);
        if (testSuccess)
        {
            _testLog.Add("All tests passed, Laci deployment verified");
            _statusFinal = CheckStatus.Success;
        }
    }

    private async Task<bool> CheckDns()
    {
        try
        {
            var hostEntryAsync = await Dns.GetHostEntryAsync(_host, CancellationToken).ConfigureAwait(false);
            var ips = hostEntryAsync.AddressList.Select(v => v.ToString()).ToArray();
            _testLog.Add($"{_host} resolved to IP(s): {string.Join(", ", ips)}");
            return true;
        }
        catch (Exception exception)
        {
            _testLog.Add($"{_host} failed to resolve: {exception.Message}");
            _log.LogError(exception, "Failed to resolve DNS for {HostName}", _host);
            return false;
        }
    }

    private async Task<bool> CheckHubExists()
    {
        try
        {
            var negotiateUrl =new UriBuilder(Uri.UriSchemeHttps, _host, _port, $"{_hubPath}/negotiate").Uri;
            var resHub = await _httpClient.GetAsync(negotiateUrl, CancellationToken).ConfigureAwait(false);
            var hubExists =
                resHub.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.OK;
            if (hubExists)
            {
                _testLog.Add($"Hub found at {negotiateUrl}");
                return true;
            }

            var content = await resHub.Content.ReadAsStringAsync(CancellationToken).ConfigureAwait(false);
            _testLog.Add($"Failed to find hub at {negotiateUrl}: {content}");
            return false;
        }
        catch (Exception exception)
        {
            _testLog.Add($"{_host} failed to connect to hub: {exception.Message}");
            _log.LogError(exception, "Failed to connect to hub {HostName}", _host);
            return false;
        }
    }

    private async Task<bool> CheckAuthentication()
    {
        var baseUri = new UriBuilder(Uri.UriSchemeHttps, _host, _port).Uri;
        var tokenUri = AuthRoutes.AuthFullPath(baseUri);
        var hashedKey = _secretKey.GetHash256();
        try
        {
            using var response = await _httpClient.PostAsync(tokenUri, new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("auth", hashedKey),
                new KeyValuePair<string, string>("charaIdent", "John Finalfantasy@CloudDC"),
            ]), CancellationToken).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync(CancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var jwt = ParseJwt(responseContent);
                var renderedClaims = jwt.Claims.Select(c => c.Type + ":" + c.Value).ToList();
                _testLog.Add(
                    $"Successfully retrieved JWT valid until {jwt.ValidTo}: {string.Join(", ", renderedClaims)}");
                _accessToken = responseContent;
                return true;
            }
            _testLog.Add($"Failed to obtain JWT via {tokenUri}. Server replied with: {responseContent}");
            return false;
        }
        catch (Exception exception)
        {
            _testLog.Add($"Failed to obtain JWT via {tokenUri}: {exception.Message}");
            _log.LogError(exception, "Failed to obtain JWT");
            return false;
        }
    }

    private async Task<bool> CheckHubConnection()
    {
        var hubUrl = new UriBuilder(Uri.UriSchemeHttps, _host, _port, _hubPath).Uri;
        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(_accessToken)!;
                    options.Transports = HttpTransportType.WebSockets;
                })
                .AddMessagePackProtocol(opt =>
                {
                    var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                        BuiltinResolver.Instance,
                        AttributeFormatterResolver.Instance,
                        DynamicEnumAsStringResolver.Instance,
                        DynamicGenericResolver.Instance,
                        DynamicUnionResolver.Instance,
                        DynamicObjectResolver.Instance,
                        PrimitiveObjectResolver.Instance,
                        StandardResolver.Instance
                    );

                    opt.SerializerOptions =
                        MessagePackSerializerOptions.Standard
                            .WithCompression(MessagePackCompression.Lz4Block)
                            .WithResolver(resolver);
                })
                .Build();
            await _hubConnection.StartAsync(CancellationToken).ConfigureAwait(false);
            _testLog.Add($"Connection to SignalR@{hubUrl} established.");
            return true;
        }
        catch (Exception e)
        {
            _testLog.Add($"Failed to establish SignalR connection to {hubUrl}");
            _log.LogError(e, "Failed to establish SignalR connection");
            return false;
        }
    }

    private async Task<bool> CheckConnectionDto()
    {
        try
        {
            var dto = await _hubConnection!.InvokeAsync<ConnectionDto>(nameof(IServerHub.GetConnectionDto), CancellationToken)
                .ConfigureAwait(false);
            _connectionDto = dto;
            var asJsonString = JsonConvert.SerializeObject(dto, Formatting.Indented);
            if (dto.User?.AliasOrUID == null)
            {
                _testLog.Add($"Failed to retrieve alias or UID. Config returned: {asJsonString}");
                return false;
            }

            if (dto.ServerInfo?.FileServerAddress == null)
            {
                _testLog.Add($"Failed to retrieve file server address. Config returned: {asJsonString}");
                return false;
            }
            _testLog.Add(
                $"Server connection working. Config returned: {asJsonString}");
            return true;
        }
        catch (Exception e)
        {
            _testLog.Add($"Failed to verify SignalR connection by requesting server info: {e.Message}");
            _log.LogError(e, "Failed to verify SignalR connection by requesting connection DTO:");
            return false;
        }
        finally
        {
            // We don't need the hub after this in any case, so it can be safely discarded now
            await DisposeHubIfNeeded().ConfigureAwait(false);
        }
    }

    private async Task<bool> CheckUploadFile()
    {
        var cdnUri = GetFilesCdnUri();
        var goatFileBytes = await File.ReadAllBytesAsync(_goatImagePath, CancellationToken).ConfigureAwait(false);
        _goatFileHash = GetFileHash(goatFileBytes);
        var fileSize = (int)new FileInfo(_goatImagePath).Length;
        var compressedFile = LZ4Wrapper.WrapHC(goatFileBytes, 0, fileSize);
        var compressedFileStream = new MemoryStream(compressedFile);
        var streamContent = new StreamContent(compressedFileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var uploadFileUri = FilesRoutes.ServerFilesUploadFullPath(cdnUri, _goatFileHash);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uploadFileUri);
        requestMessage.Content = streamContent;
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        try
        {
            var response = await _httpClient.SendAsync(requestMessage, CancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _testLog.Add($"Successfully uploaded goat image to {uploadFileUri}");
                return true;
            }
            _testLog.Add(
                $"{response.StatusCode}: Failed to upload goat image to {uploadFileUri}");
            return false;
        }
        catch (Exception e)
        {
            _testLog.Add($"Failed to upload goat image: {e.Message}");
            _log.LogError(e, "Failed to upload goat image.");
            return false;
        }
    }

    private async Task<bool> CheckDownloadFile()
    {
        try
        {
            var fileCdnUri = GetFilesCdnUri();
            var getUri = FilesRoutes.ServerFilesGetSizesFullPath(fileCdnUri);
            var content = JsonContent.Create(new List<string>([_goatFileHash]));
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, getUri);
            requestMessage.Content = content;
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var response = await _httpClient.SendAsync(requestMessage, CancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var fileSizes = await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(CancellationToken)
                    .ConfigureAwait(false) ?? [];
                var fileSize = fileSizes[0];
                _testLog.Add(
                    $"Successfully downloaded goat file info: {fileSize.Url} with size {fileSize.RawSize} bytes");
                return true;
            }
            var responseContent =
                await response.Content.ReadAsStringAsync(CancellationToken).ConfigureAwait(false);
            _testLog.Add(
                $"{response.StatusCode}: Failed to download goat image info from {getUri}: {responseContent}");
            return false;
        }
        catch (Exception e)
        {
            _testLog.Add($"Failed to download goat image: {e.Message}");
            _log.LogError(e, "Failed to download goat image.");
            return false;
        }
    }

    private Uri GetFilesCdnUri()
    {
        return _connectionDto!.ServerInfo.FileServerAddress;
    }


    private void DrawTestStatus()
    {
        var testStep = _testChain;
        while (testStep.Next != null)
        {
            DrawStatusLine(testStep.Description, testStep.CheckStatus);
            testStep = testStep.Next;
        }
    }

    private void DrawInputs()
    {
        ImGui.InputTextWithHint("Laci Host", "Your hostname, i.e. example.com", ref _host);
        ImGui.InputInt("Laci Port", ref _port);
        ImGui.InputTextWithHint("Laci Hub", "Hub name, should be 'hub'", ref _hubPath);
        ImGui.InputTextWithHint("Secret Key", "Secret key to authenticate", ref _secretKey);
    }

    private void DrawStatusLine(string text, CheckStatus status)
    {
        ImGui.TextUnformatted(text);
        ImGui.SameLine();
        var icon = StatusToIcon(status);
        var color = StatusToColor(status);
        _uiSharedService.IconText(icon, color);
    }

    private void DrawTestLog()
    {
        using var child = ImRaii.Child("test-steps", Vector2.Zero, true);
        // Check if this child is drawing
        if (child.Success)
        {
            foreach (var testLogEntry in _testLog)
            {
                ImGui.Text(testLogEntry);
            }
        }
    }

    private async Task ResetTestStatus()
    {
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        _statusFinal = CheckStatus.NotExecuted;
        _testLog = new();
        _accessToken = "";
        _connectionDto = null;
        await DisposeHubIfNeeded().ConfigureAwait(false);


        _testChain = GetTestChains();
    }

    private TestStep GetTestChains()
    {
        return new TestStep("1. DNS resolves", CheckDns,
            new TestStep("2. Check Hub Available", CheckHubExists,
                new TestStep("3. Check Authentication", CheckAuthentication,
                    new TestStep("4. Establish SignalR Connection", CheckHubConnection,
                        new TestStep("5. Get Server Info", CheckConnectionDto,
                            new TestStep("6. Upload File", CheckUploadFile,
                                new TestStep("7. Verify uploaded file", CheckDownloadFile, null)))))));
    }

    private async Task DisposeHubIfNeeded()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FontAwesomeIcon StatusToIcon(CheckStatus status)
    {
        return status switch
        {
            CheckStatus.Fail => FontAwesomeIcon.StopCircle,
            CheckStatus.Success => FontAwesomeIcon.CheckCircle,
            CheckStatus.Running => FontAwesomeIcon.Running,
            CheckStatus.NotExecuted => FontAwesomeIcon.Question,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static Vector4 StatusToColor(CheckStatus status)
    {
        return status switch
        {
            CheckStatus.Fail => ImGuiColors.DPSRed,
            CheckStatus.Success => ImGuiColors.HealerGreen,
            CheckStatus.Running => ImGuiColors.DalamudOrange,
            CheckStatus.NotExecuted => ImGuiColors.ParsedGrey,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static JwtSecurityToken ParseJwt(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ReadJwtToken(token);
    }

    private static string GetFileHash(byte[] fileContentUncompressed)
    {
        return Convert.ToHexString(SHA1.HashData(fileContentUncompressed)).ToUpperInvariant();
    }
    
    internal class TestStep(string description, Func<Task<bool>> testStep, TestStep? next)
    {
        public readonly TestStep? Next = next;
        public readonly string Description = description;
        public CheckStatus CheckStatus { private set; get; } = CheckStatus.NotExecuted;

        public async Task<bool> Execute()
        {
            CheckStatus = CheckStatus.Running;
            bool success = await testStep().ConfigureAwait(false);
            if (!success)
            {
                CheckStatus = CheckStatus.Fail;
                return false;
            }

            CheckStatus = CheckStatus.Success;
            if (Next == null)
            {
                return true;
            }
            return await Next.Execute().ConfigureAwait(false);
        }
    }
    
    internal enum CheckStatus
    {
        NotExecuted,
        Running,
        Success,
        Fail
    }
}