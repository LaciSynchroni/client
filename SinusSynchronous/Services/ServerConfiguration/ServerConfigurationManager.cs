using Dalamud.Utility;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using SinusSynchronous.API.Routes;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.SinusConfiguration;
using SinusSynchronous.SinusConfiguration.Models;
using SinusSynchronous.WebAPI;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SinusSynchronous.Services.ServerConfiguration;

public class ServerConfigurationManager
{
    private readonly ServerConfigService _serverConfigService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly SinusConfigService _sinusConfigService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerConfigurationManager> _logger;
    private readonly SinusMediator _sinusMediator;
    private readonly NotesConfigService _notesConfig;
    private readonly ServerTagConfigService _serverTagConfig;

    public ServerConfigurationManager(ILogger<ServerConfigurationManager> logger, ServerConfigService serverConfigService,
        ServerTagConfigService serverTagConfig, NotesConfigService notesConfig, DalamudUtilService dalamudUtil,
        SinusConfigService sinusConfigService, HttpClient httpClient, SinusMediator sinusMediator)
    {
        _logger = logger;
        _serverConfigService = serverConfigService;
        _serverTagConfig = serverTagConfig;
        _notesConfig = notesConfig;
        _dalamudUtil = dalamudUtil;
        _sinusConfigService = sinusConfigService;
        _httpClient = httpClient;
        _sinusMediator = sinusMediator;
        EnsureMainExists();
    }

    public IEnumerable<int> ServerIndexes => _serverConfigService.Current.ServerStorage.Select((_, i) => i);
    
    public string CurrentApiUrl => CurrentServer.ServerUri;
    public ServerStorage CurrentServer => _serverConfigService.Current.ServerStorage[CurrentServerIndex];
    public bool SendCensusData
    {
        get
        {
            return _serverConfigService.Current.SendCensusData;
        }
        set
        {
            _serverConfigService.Current.SendCensusData = value;
            _serverConfigService.Save();
        }
    }

    public bool ShownCensusPopup
    {
        get
        {
            return _serverConfigService.Current.ShownCensusPopup;
        }
        set
        {
            _serverConfigService.Current.ShownCensusPopup = value;
            _serverConfigService.Save();
        }
    }

    public int CurrentServerIndex
    {
        set
        {
            _serverConfigService.Current.CurrentServer = value;
            _serverConfigService.Save();
        }
        get
        {
            if (_serverConfigService.Current.CurrentServer < 0)
            {
                _serverConfigService.Current.CurrentServer = 0;
                _serverConfigService.Save();
            }

            return _serverConfigService.Current.CurrentServer;
        }
    }

    public bool ShowServerPickerInMainMenu
    {
        set
        {
            _serverConfigService.Current.ShowServerPickerInMainMenu = value;
            _serverConfigService.Save();
        }
        get
        {
            return _serverConfigService.Current.ShowServerPickerInMainMenu;
        }
    }

    public bool EnableMultiConnect => _serverConfigService.Current.EnableMultiConnect;

    public (string OAuthToken, string UID)? GetOAuth2(out bool hasMulti, int serverIdx = -1)
    {
        ServerStorage? currentServer;
        currentServer = serverIdx == -1 ? CurrentServer : GetServerByIndex(serverIdx);
        if (currentServer == null)
        {
            currentServer = new();
            Save();
        }
        hasMulti = false;

        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var cid = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult();

        var auth = currentServer.Authentications.FindAll(f => string.Equals(f.CharacterName, charaName) && f.WorldId == worldId);
        if (auth.Count >= 2)
        {
            _logger.LogTrace("GetOAuth2 accessed, returning null because multiple ({count}) identical characters.", auth.Count);
            hasMulti = true;
            return null;
        }

        if (auth.Count == 0)
        {
            _logger.LogTrace("GetOAuth2 accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
            return null;
        }

        if (auth.Single().LastSeenCID != cid)
        {
            auth.Single().LastSeenCID = cid;
            _logger.LogTrace("GetOAuth2 accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
            Save();
        }

        if (!string.IsNullOrEmpty(auth.Single().UID) && !string.IsNullOrEmpty(currentServer.OAuthToken))
        {
            _logger.LogTrace("GetOAuth2 accessed, returning {key} ({keyValue}) for {chara} on {world}", auth.Single().UID, string.Join("", currentServer.OAuthToken.Take(10)), charaName, worldId);
            return (currentServer.OAuthToken, auth.Single().UID!);
        }

        _logger.LogTrace("GetOAuth2 accessed, returning null because no UID found for {chara} on {world} or OAuthToken is not configured.", charaName, worldId);

        return null;
    }

    public string? GetSecretKey(out bool hasMulti, int serverIdx = -1)
    {
        ServerStorage? currentServer;
        currentServer = serverIdx == -1 ? CurrentServer : GetServerByIndex(serverIdx);
        if (currentServer == null)
        {
            currentServer = new();
            Save();
        }
        hasMulti = false;

        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var cid = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult();
        if (!currentServer.Authentications.Any() && currentServer.SecretKeys.Any())
        {
            currentServer.Authentications.Add(new Authentication()
            {
                CharacterName = charaName,
                WorldId = worldId,
                LastSeenCID = cid,
                SecretKeyIdx = currentServer.SecretKeys.Last().Key,
            });

            Save();
        }

        var auth = currentServer.Authentications.FindAll(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
        if (auth.Count >= 2)
        {
            _logger.LogTrace("GetSecretKey accessed, returning null because multiple ({count}) identical characters.", auth.Count);
            hasMulti = true;
            return null;
        }

        if (auth.Count == 0)
        {
            _logger.LogTrace("GetSecretKey accessed, returning null because no set up characters for {chara} on {world}", charaName, worldId);
            return null;
        }

        if (auth.Single().LastSeenCID != cid)
        {
            auth.Single().LastSeenCID = cid;
            _logger.LogTrace("GetSecretKey accessed, updating CID for {chara} on {world} to {cid}", charaName, worldId, cid);
            Save();
        }

        if (currentServer.SecretKeys.TryGetValue(auth.Single().SecretKeyIdx, out var secretKey))
        {
            _logger.LogTrace("GetSecretKey accessed, returning {key} ({keyValue}) for {chara} on {world}", secretKey.FriendlyName, string.Join("", secretKey.Key.Take(10)), charaName, worldId);
            return secretKey.Key;
        }

        _logger.LogTrace("GetSecretKey accessed, returning null because no fitting key found for {chara} on {world} for idx {idx}.", charaName, worldId, auth.Single().SecretKeyIdx);

        return null;
    }

    public string[] GetServerApiUrls()
    {
        return _serverConfigService.Current.ServerStorage.Select(v => v.ServerUri).ToArray();
    }

    public ServerStorage GetServerByIndex(int idx)
    {
        try
        {
            return _serverConfigService.Current.ServerStorage[idx];
        }
        catch
        {
            _serverConfigService.Current.CurrentServer = 0;
            EnsureMainExists();
            return CurrentServer!;
        }
    }

    public string GetDiscordUserFromToken(ServerStorage server)
    {
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
        if (string.IsNullOrEmpty(server.OAuthToken)) return string.Empty;
        try
        {
            var token = handler.ReadJwtToken(server.OAuthToken);
            return token.Claims.First(f => string.Equals(f.Type, "discord_user", StringComparison.Ordinal)).Value!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read jwt, resetting it");
            server.OAuthToken = null;
            Save();
            return string.Empty;
        }
    }

    public List<ServerInfoDto> GetServerInfo()
    {
        var items = _serverConfigService.Current.ServerStorage
            .Select((v, index) => new ServerInfoDto
            {
                Id = index,
                Name = v.ServerName,
                ServerUri = v.ServerUri,
            }).ToList();
        return items;
    }

    public string[] GetServerNames()
    {
        return _serverConfigService.Current.ServerStorage.Select(v => v.ServerName).ToArray();
    }

    public bool HasValidConfig()
    {
        return CurrentServer != null && CurrentServer.Authentications.Count > 0;
    }

    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        _logger.LogDebug("{caller} Calling config save", caller);
        _serverConfigService.Save();
    }

    public void SelectServer(int idx)
    {
        _serverConfigService.Current.CurrentServer = idx;
        CurrentServer!.FullPause = false;
        Save();
    }

    internal void AddCurrentCharacterToServer(int serverSelectionIndex = -1)
    {
        if (serverSelectionIndex == -1) serverSelectionIndex = CurrentServerIndex;
        var server = GetServerByIndex(serverSelectionIndex);
        if (server.Authentications.Any(c => string.Equals(c.CharacterName, _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(), StringComparison.Ordinal)
                && c.WorldId == _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult()))
            return;

        server.Authentications.Add(new Authentication()
        {
            CharacterName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(),
            WorldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult(),
            SecretKeyIdx = !server.UseOAuth2 ? server.SecretKeys.Last().Key : -1,
            LastSeenCID = _dalamudUtil.GetCIDAsync().GetAwaiter().GetResult()
        });
        Save();
    }

    internal void AddEmptyCharacterToServer(int serverSelectionIndex)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication()
        {
            SecretKeyIdx = server.SecretKeys.Any() ? server.SecretKeys.First().Key : -1,
        });
        Save();
    }

    internal void AddOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Add(tag);
        _serverTagConfig.Save();
    }

    internal void AddServer(ServerStorage serverStorage)
    {
        _serverConfigService.Current.ServerStorage.Add(serverStorage);
        Save();
    }

    internal void AddTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Add(tag);
        _serverTagConfig.Save();
        _sinusMediator.Publish(new RefreshUiMessage());
    }

    internal void AddTagForUid(string uid, string tagName)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Add(tagName);
            _sinusMediator.Publish(new RefreshUiMessage());
        }
        else
        {
            CurrentServerTagStorage().UidServerPairedUserTags[uid] = [tagName];
        }

        _serverTagConfig.Save();
    }

    internal bool ContainsOpenPairTag(string tag)
    {
        return CurrentServerTagStorage().OpenPairTags.Contains(tag);
    }

    internal bool ContainsTag(string uid, string tag)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Contains(tag, StringComparer.Ordinal);
        }

        return false;
    }

    internal void DeleteServer(ServerStorage selectedServer)
    {
        if (Array.IndexOf(_serverConfigService.Current.ServerStorage.ToArray(), selectedServer) <
            _serverConfigService.Current.CurrentServer)
        {
            _serverConfigService.Current.CurrentServer--;
        }

        _serverConfigService.Current.ServerStorage.Remove(selectedServer);
        Save();
    }

    internal string? GetNoteForGid(string gID)
    {
        if (CurrentNotesStorage().GidServerComments.TryGetValue(gID, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }

        return null;
    }

    internal string? GetNoteForUid(string uid)
    {
        if (CurrentNotesStorage().UidServerComments.TryGetValue(uid, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }
        return null;
    }

    internal HashSet<string> GetServerAvailablePairTags()
    {
        return CurrentServerTagStorage().ServerAvailablePairTags;
    }

    internal Dictionary<string, List<string>> GetUidServerPairedUserTags()
    {
        return CurrentServerTagStorage().UidServerPairedUserTags;
    }

    internal HashSet<string> GetUidsForTag(string tag)
    {
        return CurrentServerTagStorage().UidServerPairedUserTags.Where(p => p.Value.Contains(tag, StringComparer.Ordinal)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }

    internal bool HasTags(string uid)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Any();
        }

        return false;
    }

    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Remove(item);
        Save();
    }

    internal void RemoveOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    internal void RemoveTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Remove(tag);
        foreach (var uid in GetUidsForTag(tag))
        {
            RemoveTagForUid(uid, tag, save: false);
        }
        _serverTagConfig.Save();
        _sinusMediator.Publish(new RefreshUiMessage());
    }

    internal void RemoveTagForUid(string uid, string tagName, bool save = true)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Remove(tagName);

            if (save)
            {
                _serverTagConfig.Save();
                _sinusMediator.Publish(new RefreshUiMessage());
            }
        }
    }

    internal void RenameTag(string oldName, string newName)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Remove(oldName);
        CurrentServerTagStorage().ServerAvailablePairTags.Add(newName);
        foreach (var existingTags in CurrentServerTagStorage().UidServerPairedUserTags.Select(k => k.Value))
        {
            if (existingTags.Remove(oldName))
                existingTags.Add(newName);
        }
    }

    internal void SaveNotes()
    {
        _notesConfig.Save();
    }

    internal void SetNoteForGid(string gid, string note, bool save = true)
    {
        if (string.IsNullOrEmpty(gid)) return;

        CurrentNotesStorage().GidServerComments[gid] = note;
        if (save)
            _notesConfig.Save();
    }

    internal void SetNoteForUid(string uid, string note, bool save = true)
    {
        if (string.IsNullOrEmpty(uid)) return;

        CurrentNotesStorage().UidServerComments[uid] = note;
        if (save)
            _notesConfig.Save();
    }

    internal void AutoPopulateNoteForUid(string uid, string note)
    {
        if (!_sinusConfigService.Current.AutoPopulateEmptyNotesFromCharaName
            || GetNoteForUid(uid) != null)
            return;

        SetNoteForUid(uid, note, save: true);
    }

    private ServerNotesStorage CurrentNotesStorage()
    {
        TryCreateCurrentNotesStorage();
        return _notesConfig.Current.ServerNotes[CurrentApiUrl];
    }

    private ServerTagStorage CurrentServerTagStorage()
    {
        TryCreateCurrentServerTagStorage();
        return _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl];
    }

    private void EnsureMainExists()
    {
        if (_serverConfigService.Current.ServerStorage.Count == 0 || !string.Equals(_serverConfigService.Current.ServerStorage[0].ServerUri, ApiController.MainServiceUri, StringComparison.OrdinalIgnoreCase))
        {
            _serverConfigService.Current.ServerStorage.Insert(0, new ServerStorage() { ServerUri = ApiController.MainServiceUri, ServerName = ApiController.MainServer, UseOAuth2 = true });
        }
        Save();
    }

    private void TryCreateCurrentNotesStorage()
    {
        if (!_notesConfig.Current.ServerNotes.ContainsKey(CurrentApiUrl))
        {
            _notesConfig.Current.ServerNotes[CurrentApiUrl] = new();
        }
    }

    private void TryCreateCurrentServerTagStorage()
    {
        if (!_serverTagConfig.Current.ServerTagStorage.ContainsKey(CurrentApiUrl))
        {
            _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl] = new();
        }
    }

    public async Task<Dictionary<string, string>> GetUIDsWithDiscordToken(string serverUri, string token)
    {
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = SinusAuth.GetUIDsFullPath(new Uri(baseUri));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.GetAsync(oauthCheckUri).ConfigureAwait(false);
            var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(responseStream).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure getting UIDs");
            return [];
        }
    }

    public async Task<Uri?> CheckDiscordOAuth(string serverUri)
    {
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = SinusAuth.GetDiscordOAuthEndpointFullPath(new Uri(baseUri));
            var response = await _httpClient.GetFromJsonAsync<Uri?>(oauthCheckUri).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure checking for Discord Auth");
            return null;
        }
    }

    public async Task<string?> GetDiscordOAuthToken(Uri discordAuthUri, string serverUri, CancellationToken token)
    {
        var sessionId = BitConverter.ToString(RandomNumberGenerator.GetBytes(64)).Replace("-", "").ToLower();
        Util.OpenLink(discordAuthUri.ToString() + "?sessionId=" + sessionId);

        string? discordToken = null;
        using CancellationTokenSource timeOutCts = new();
        timeOutCts.CancelAfter(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeOutCts.Token, token);
        try
        {
            var baseUri = serverUri.Replace("wss://", "https://").Replace("ws://", "http://");
            var oauthCheckUri = SinusAuth.GetDiscordOAuthTokenFullPath(new Uri(baseUri), sessionId);
            var response = await _httpClient.GetAsync(oauthCheckUri, linkedCts.Token).ConfigureAwait(false);
            discordToken = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure getting Discord Token");
            return null;
        }

        if (discordToken == null)
            return null;

        return discordToken;
    }

    public HttpTransportType GetTransport()
    {
        return CurrentServer.HttpTransportType;
    }

    public void SetTransportType(HttpTransportType httpTransportType)
    {
        CurrentServer.HttpTransportType = httpTransportType;
        Save();
    }
}