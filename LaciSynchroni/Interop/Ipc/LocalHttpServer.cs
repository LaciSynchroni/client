using LaciSynchroni.Common.Data;
using LaciSynchroni.Services.Mediator;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration.Models;
using LaciSynchroni.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Net;
using System.Web;
using NotificationMessage = LaciSynchroni.Services.Mediator.NotificationMessage;

namespace LaciSynchroni.Interop.Ipc;

/// <summary>
/// Local HTTP server that listens for server join requests via browser links
/// Inspired by Heliosphere's implementation
/// </summary>
public class LocalHttpServer : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<LocalHttpServer> _logger;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly SyncMediator _mediator;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public bool Enabled => _listener?.IsListening ?? false;

    public SyncMediator Mediator => _mediator;

    public LocalHttpServer(
        ILogger<LocalHttpServer> logger,
        ServerConfigurationManager serverConfigurationManager,
        SyncMediator mediator)
    {
        _logger = logger;
        _serverConfigurationManager = serverConfigurationManager;
        _mediator = mediator;

        Mediator.Subscribe<HttpServerToggleMessage>(this, HandleToggleRequest);

        // this feels terrible but it does the job of immediately stopping it
        Mediator.Publish(new HttpServerToggleMessage(false));
    }

    private void HandleToggleRequest(HttpServerToggleMessage message)
    {
        if (message.enable && !Enabled)
        {
            _ = Task.Run(async () =>
            {
                await StartAsync(default).ConfigureAwait(false);
            });
        }
        else if (!message.enable && Enabled)
        {
            _ = Task.Run(async () =>
            {
                await StopAsync(default).ConfigureAwait(false);
            });
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{PluginHttpServerData.Hostname}:{PluginHttpServerData.Port}/");
            _listener.Start();
            
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
            
            _logger.LogInformation("Local HTTP server started on port {Port}", PluginHttpServerData.Port);
            _logger.LogInformation("Server join links: {Prefix}:{Port}/laci/join?name=...&uri=...", PluginHttpServerData.Hostname, PluginHttpServerData.Port);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogWarning(ex, "Failed to start HTTP server on port {Port}. Server join links will not work.", PluginHttpServerData.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting HTTP server");
        }
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
            
            _logger.LogInformation("Local HTTP server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping HTTP server");
        }
        
        return Task.CompletedTask;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context), token);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HTTP listener loop");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            
            _logger.LogInformation("Received request: {Method} {Url}", request.HttpMethod, request.Url);

            // Parse the request URL
            var path = request.Url?.AbsolutePath.TrimStart('/');
            
            if (string.IsNullOrEmpty(path))
            {
                await SendResponseAsync(response, 400, "Invalid request").ConfigureAwait(false);
                return;
            }

            var parts = path.Split('/');
            if (parts.Length < 2 || !string.Equals(parts[0], "laci", StringComparison.OrdinalIgnoreCase))
            {
                await SendResponseAsync(response, 404, "Not found").ConfigureAwait(false);
                return;
            }

            var action = parts[1].ToLowerInvariant();
            var queryParams = HttpUtility.ParseQueryString(request.Url?.Query ?? string.Empty);

            if (string.Equals(action, "join", StringComparison.OrdinalIgnoreCase))
            {
                HandleJoinServer(queryParams);
                await SendResponseAsync(response, 200, 
                    "<html><body><h1>Success!</h1><p>Check your game - a dialog should have appeared to add the server.</p><p>You can close this tab.</p></body></html>",
                    "text/html").ConfigureAwait(false);
            }
            else
            {
                await SendResponseAsync(response, 400, $"Unknown action: {action}").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling HTTP request");
            try
            {
                await SendResponseAsync(context.Response, 500, "Internal server error").ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors sending error response
            }
        }
    }

    private void HandleJoinServer(System.Collections.Specialized.NameValueCollection queryParams)
    {
        var serverUri = queryParams["uri"];
        var secretKey = queryParams["secretkey"];

        if (string.IsNullOrEmpty(serverUri))
        {
            _logger.LogWarning("Missing required parameters for server join");
            Mediator.Publish(new NotificationMessage("Invalid Link", "Server link is missing required information (URI).", NotificationType.Warning));
            return;
        }

        // Normalize the base URI (remove trailing slashes and /hub if present)
        var normalizedUri = serverUri.TrimEnd('/');
        if (normalizedUri.EndsWith("/hub", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUri = normalizedUri.Substring(0, normalizedUri.Length - 4).TrimEnd('/');
        }

        // Check if server already exists
        var existingServers = _serverConfigurationManager.GetServerInfo();
        var existingServerWithUri = existingServers.FirstOrDefault(s => string.Equals(s.Uri, normalizedUri, StringComparison.OrdinalIgnoreCase));
        if (existingServerWithUri != null)
        {
            _logger.LogInformation("Server already exists: {ServerName}", existingServerWithUri.Name);
            Mediator.Publish(new NotificationMessage("Server Exists", $"The server '{existingServerWithUri.Name}' is already configured.", NotificationType.Info));
            return;
        }

        // Create server storage object - matches the pattern from add server UI
        var newServer = new ServerStorage
        {
            ServerUri = normalizedUri,
            UseOAuth2 = true,
            UseAdvancedUris = false,
            SecretKeys = { { 0, new SecretKey() { FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})", Key = secretKey } } }
        };

        // Publish message to show confirmation UI
        Mediator.Publish(new ServerJoinRequestMessage(newServer));
        
        _logger.LogInformation("Server join request created for {ServerUri}", normalizedUri);
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string content, string contentType = "text/plain")
    {
        try
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            var buffer = System.Text.Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
            response.OutputStream.Close();
        }
        catch (Exception)
        {
            // Ignore errors writing response
        }
    }
}

/// <summary>
/// Message published when a server join is requested via URI
/// </summary>
public record ServerJoinRequestMessage(ServerStorage ServerStorage) : MessageBase;

/// <summary>
/// Message published when the state of the 
/// </summary>
public record HttpServerToggleMessage(bool enable) : MessageBase;