using Microsoft.Extensions.Logging;
using SinusSynchronous.API.Dto;
using SinusSynchronous.Services;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.Services.ServerConfiguration;
using SinusSynchronous.SinusConfiguration.Models;
using SinusSynchronous.WebAPI.SignalR;
using System.Collections.Concurrent;

namespace SinusSynchronous.WebAPI
{
    using ServerIndex = int;
    
    public class MultiConnectTokenService
    {
        private readonly ConcurrentDictionary<ServerIndex, MultiConnectTokenProvider> _tokenProviders;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ServerConfigurationManager _serverConfigurationManager;
        private readonly DalamudUtilService _dalamudUtilService;
        private readonly SinusMediator _sinusMediator;
        private readonly HttpClient _httpClient;
        
        public MultiConnectTokenService(HttpClient httpClient, SinusMediator sinusMediator, DalamudUtilService dalamudUtilService, ILoggerFactory loggerFactory, ServerConfigurationManager serverConfigurationManager)
        {
            _httpClient = httpClient;
            _sinusMediator = sinusMediator;
            _dalamudUtilService = dalamudUtilService;
            _loggerFactory = loggerFactory;
            _serverConfigurationManager = serverConfigurationManager;
            _tokenProviders = new ConcurrentDictionary<ServerIndex, MultiConnectTokenProvider>();
        }

        public Task<string?> GetCachedToken(ServerIndex serverIndex)
        {
            return GetTokenProvider(serverIndex).GetToken();
        }

        public Task<string?> GetOrUpdateToken(ServerIndex serverIndex, CancellationToken ct)
        {
            return GetTokenProvider(serverIndex).GetOrUpdateToken(ct);
        }

        public Task<bool> TryUpdateOAuth2LoginTokenAsync(ServerIndex serverIndex, ServerStorage currentServer, bool forced = false)
        {
            return GetTokenProvider(serverIndex).TryUpdateOAuth2LoginTokenAsync(currentServer, forced);
        }
        
        private MultiConnectTokenProvider GetTokenProvider(ServerIndex serverIndex)
        {
            return _tokenProviders.GetOrAdd(serverIndex, BuildNewTokenProvider);
        }

        private MultiConnectTokenProvider BuildNewTokenProvider(ServerIndex serverIndex)
        {
            return new MultiConnectTokenProvider(
                _loggerFactory.CreateLogger<MultiConnectTokenProvider>(),
                serverIndex,
                _serverConfigurationManager,
                _dalamudUtilService,
                _sinusMediator,
                _httpClient
            );
        }
    }
}