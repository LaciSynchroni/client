using Microsoft.Extensions.Logging;
using SinusSynchronous.API.Dto.Group;
using SinusSynchronous.PlayerData.Pairs;
using SinusSynchronous.Services;
using SinusSynchronous.Services.Mediator;
using SinusSynchronous.Services.ServerConfiguration;
using SinusSynchronous.SinusConfiguration;
using SinusSynchronous.UI.Components;
using SinusSynchronous.UI.Handlers;
using SinusSynchronous.WebAPI;
using System.Collections.Immutable;

namespace SinusSynchronous.UI;

public class DrawEntityFactory
{
    private readonly ILogger<DrawEntityFactory> _logger;
    private readonly ApiController _apiController;
    private readonly SinusMediator _mediator;
    private readonly SelectPairForTagUi _selectPairForTagUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly CharaDataManager _charaDataManager;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;

    public DrawEntityFactory(ILogger<DrawEntityFactory> logger, ApiController apiController, IdDisplayHandler uidDisplayHandler,
        SelectTagForPairUi selectTagForPairUi, SinusMediator mediator,
        TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi,
        ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService,
        PlayerPerformanceConfigService playerPerformanceConfigService, CharaDataManager charaDataManager)
    {
        _logger = logger;
        _apiController = apiController;
        _uidDisplayHandler = uidDisplayHandler;
        _selectTagForPairUi = selectTagForPairUi;
        _mediator = mediator;
        _tagHandler = tagHandler;
        _selectPairForTagUi = selectPairForTagUi;
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _charaDataManager = charaDataManager;
    }

    public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoWithServer groupFullInfoDto,
        Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs,
        IImmutableList<Pair> allPairs)
    {
        var imguiId = groupFullInfoDto.GroupFullInfo.GID + groupFullInfoDto.ServerIndex;
        var pairsToRender = filteredPairs.Select(p => CreateDrawPair(groupFullInfoDto, p)).ToImmutableList();
        return new DrawFolderGroup(imguiId, groupFullInfoDto.ServerIndex, groupFullInfoDto.GroupFullInfo, _apiController,
            pairsToRender,
            allPairs, _tagHandler, _uidDisplayHandler, _mediator, _uiSharedService);
    }

    public DrawFolderTag CreateDrawTagFolder(string tag,
        int serverIndex,
        Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs,
        IImmutableList<Pair> allPairs)
    {
        return new(tag, filteredPairs.Select(u => CreateDrawPair(tag, serverIndex, u.Key, u.Value, null)).ToImmutableList(),
            allPairs, _tagHandler, _apiController, _selectPairForTagUi, _uiSharedService);
    }
    
    public DrawUserPair CreateDrawPair(string id, int serverIndex, Pair user, List<GroupFullInfoDto> groups, GroupFullInfoDto? currentGroup)
    {
        return new DrawUserPair(id + user.UserData.UID, serverIndex, user, groups, currentGroup, _apiController, _uidDisplayHandler,
            _mediator, _selectTagForPairUi, _serverConfigurationManager, _uiSharedService, _playerPerformanceConfigService,
            _charaDataManager);
    }

    private DrawUserPair CreateDrawPair(GroupFullInfoWithServer groupFullInfoWithServer, KeyValuePair<Pair, List<GroupFullInfoDto>> filteredPairs)
    {
        var serverIndex = groupFullInfoWithServer.ServerIndex;
        var pair = filteredPairs.Key;
        var groups = filteredPairs.Value;
        var id = groupFullInfoWithServer.GroupFullInfo.Group.GID + pair.UserData.UID;
        return CreateDrawPair(id, serverIndex, pair, groups, groupFullInfoWithServer.GroupFullInfo);
    }
    
}