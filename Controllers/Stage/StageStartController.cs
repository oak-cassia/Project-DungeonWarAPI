﻿using DungeonWarAPI.DatabaseAccess;
using DungeonWarAPI.DatabaseAccess.Interfaces;
using Microsoft.AspNetCore.Mvc;
using DungeonWarAPI.Enum;
using DungeonWarAPI.GameLogic;
using DungeonWarAPI.Models.Database.Game;
using ZLogger;
using DungeonWarAPI.Models.DTO.RequestResponse.Stage;
using DungeonWarAPI.Models.DAO.Redis;

namespace DungeonWarAPI.Controllers.Stage;

[Route("[controller]")]
[ApiController]
public class StageStartController : ControllerBase
{
    private readonly IStageDataCRUD _stageDataCRUD;
    private readonly MasterDataProvider _masterDataProvider;
    private readonly IMemoryDatabase _memoryDatabase;
    private readonly ILogger<StageStartController> _logger;

    public StageStartController(ILogger<StageStartController> logger, IMemoryDatabase memoryDatabase,
        MasterDataProvider masterDataProvider,
        IStageDataCRUD stageDataCRUD)
    {
        _memoryDatabase = memoryDatabase;
        _stageDataCRUD = stageDataCRUD;
        _masterDataProvider = masterDataProvider;
        _logger = logger;
    }

    [HttpPost]
    public async Task<StageStartResponse> Post(StageStartRequest request)
    {
        var userAuthAndState = HttpContext.Items[nameof(AuthenticatedUserState)] as AuthenticatedUserState;
        var response = new StageStartResponse();

        var gameUserId = userAuthAndState.GameUserId;
        var selectedStageLevel = request.SelectedStageLevel;

        var errorCode = await CheckStageAccessibilityAsync(gameUserId, selectedStageLevel, userAuthAndState.State);
        if (errorCode != 0)
        {
            response.Error = errorCode;
            return response;
        }

        var itemList = _masterDataProvider.GetStageItemList(selectedStageLevel);
        var npcList = _masterDataProvider.GetStageNpcList(selectedStageLevel);
        if (!itemList.Any() || !npcList.Any())
        {
            response.Error = ErrorCode.WrongStageLevel;
            return response;
        }

        errorCode = await StoreInitialStageDataAsync(userAuthAndState, itemList, npcList, selectedStageLevel);
        if (errorCode != ErrorCode.None)
        {
            response.Error = errorCode;
            return response;
        }

        response.ItemList = itemList;
        response.NpcList = npcList.ToList();
        response.Error = ErrorCode.None;
        return response;
    }

    private async Task<ErrorCode> CheckStageAccessibilityAsync(int gameUserId, int selectedStageLevel,
        UserStateCode state)
    {
        if (state != UserStateCode.Lobby)
        {
            _logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.WrongUserState, GameUserId = gameUserId },
                "CheckStageAccessibility");
            return ErrorCode.WrongUserState;
        }

        var (errorCode, maxClearedStage) = await _stageDataCRUD.LoadStageListAsync(gameUserId);
        if (errorCode != ErrorCode.None)
        {
            return errorCode;
        }

        errorCode = StageInitializer.VerifyAccessibility(maxClearedStage, selectedStageLevel);
        if (errorCode != ErrorCode.None)
        {
            return errorCode;
        }

        return errorCode;
    }

    private async Task<ErrorCode> StoreInitialStageDataAsync(AuthenticatedUserState authenticatedUserState,
        List<StageItem> itemList, List<StageNpc> npcList, int selectedStageLevel)
    {
        var stageKey = MemoryDatabaseKeyGenerator.MakeStageKey(authenticatedUserState.Email);
        var stageKeyValueList = StageInitializer.CreateInitialKeyValue(itemList, npcList, selectedStageLevel);

        var errorCode = await _memoryDatabase.InsertStageDataAsync(stageKey, stageKeyValueList);
        if (errorCode != ErrorCode.None)
        {
            return errorCode;
        }

        var userKey = MemoryDatabaseKeyGenerator.MakeUIDKey(authenticatedUserState.Email);
        errorCode = await _memoryDatabase.UpdateUserStateAsync(userKey, authenticatedUserState, UserStateCode.InStage);
        if (errorCode != ErrorCode.None)
        {
            return errorCode;
        }

        return ErrorCode.None;
    }
}