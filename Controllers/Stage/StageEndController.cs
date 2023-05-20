﻿using DungeonWarAPI.DatabaseAccess;
using DungeonWarAPI.DatabaseAccess.Interfaces;
using DungeonWarAPI.Enum;
using DungeonWarAPI.GameLogic;
using DungeonWarAPI.Models.DAO.Redis;
using DungeonWarAPI.Models.DTO.RequestResponse.Stage;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ZLogger;

namespace DungeonWarAPI.Controllers.Stage;

[Route("[controller]")]
[ApiController]
public class StageEndController : ControllerBase
{
    private readonly IDungeonStageService _dungeonStageService;
    private readonly IItemService _itemService;
    private readonly MasterDataManager _masterDataManager;
    private readonly IMemoryDatabase _memoryDatabase;
    private readonly ILogger<StageEndController> _logger;

    public StageEndController(ILogger<StageEndController> logger, IMemoryDatabase memoryDatabase,
        MasterDataManager masterDataManager,
        IDungeonStageService dungeonStageService, IItemService itemService)
    {
        _memoryDatabase = memoryDatabase;
        _dungeonStageService = dungeonStageService;
        _itemService = itemService;
        _masterDataManager = masterDataManager;
        _logger = logger;
    }

    [HttpPost]
    public async Task<StageEndResponse> Post(StageEndRequest request)
    {
        var userAuthAndState = HttpContext.Items[nameof(AuthenticatedUserState)] as AuthenticatedUserState;
        var response = new StageEndResponse();
        var gameUserId = userAuthAndState.GameUserId;
        var state = userAuthAndState.State;
        var key = MemoryDatabaseKeyGenerator.MakeStageKey(request.Email);

        var (errorCode, dictionary) = await LoadStageDataIfUserInStageAsync(key, gameUserId, state);
        if (errorCode != ErrorCode.None)
        {
            response.Error = errorCode;
            return response;
        }

        var (itemCodeAndCount, npcCodeAndCount, stageLevel) = StageDataParser.ParseStageData(dictionary);

        var stageNpcList = _masterDataManager.GetStageNpcList(stageLevel);
        var (isCleared, earnedExp) = StageRequestVerifier.VerifyClearAndCalcExp(stageNpcList, npcCodeAndCount);

        if (isCleared == false)
        {
            //await _memoryDatabase.DeleteStageDataAsync(key);
            response.IsCleared = isCleared;
            response.Error = ErrorCode.None;
            return response;
        }

        var userKey = MemoryDatabaseKeyGenerator.MakeUIDKey(request.Email);
        errorCode = await _memoryDatabase.UpdateUserStateAsync(userKey, userAuthAndState, UserStateCode.Lobby);
        if (errorCode != ErrorCode.None)
        {
            response.Error = errorCode;
            return response;
        }

        errorCode = await ProcessStageCompletionAsync(gameUserId, stageLevel, earnedExp, itemCodeAndCount);
        if (errorCode != ErrorCode.None)
        {
            await _memoryDatabase.UpdateUserStateAsync(userKey, userAuthAndState, UserStateCode.InStage);
            response.Error = errorCode;
            return response;
        }

        await _memoryDatabase.DeleteStageDataAsync(key);
        response.IsCleared = isCleared;
        response.Error = ErrorCode.None;
        return response;
    }

    private async Task<(ErrorCode, Dictionary<string, int>)> LoadStageDataIfUserInStageAsync(string key, int gameUserId, UserStateCode state)
    {
        if (state != UserStateCode.InStage)
        {
            _logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.WrongUserState, GameUserId = gameUserId },
                "CheckStageAccessibility");
            return (ErrorCode.WrongUserState, new Dictionary<string, int>());
        }

        var (errorCode, dictionary) = await _memoryDatabase.LoadStageDataAsync(key);
        if (errorCode != ErrorCode.None)
        {
            return (errorCode, new Dictionary<string, int>());
        }

        return (ErrorCode.None, dictionary);
    }

    private async Task<ErrorCode> ProcessStageCompletionAsync(int gameUserId, int stageLevel, int earnedExp, List<(int, int)> itemCodeAndCount)
    {
        var (errorCode, isIncrement) = await _dungeonStageService.IncreaseMaxClearedStageAsync(gameUserId, stageLevel);
        if (errorCode != ErrorCode.None)
        {
            return errorCode;
        }

        (errorCode, int existingLevel, int existingExp) = await _dungeonStageService.UpdateExpAsync(gameUserId, earnedExp);
        if (errorCode != ErrorCode.None)
        {
            await _dungeonStageService.RollbackIncreaseMaxClearedStageAsync(gameUserId, isIncrement);

            return errorCode;
        }

        //우편함에 주는 것 고려 정책
        errorCode = await _itemService.InsertItemsAsync(gameUserId, itemCodeAndCount);
        if (errorCode != ErrorCode.None)
        {
            await _dungeonStageService.RollbackUpdateExpAsync(gameUserId, existingLevel, existingExp);
            await _dungeonStageService.RollbackIncreaseMaxClearedStageAsync(gameUserId, isIncrement);

            return errorCode;
        }

        return ErrorCode.None;
    }

}