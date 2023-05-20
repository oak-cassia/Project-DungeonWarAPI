﻿using DungeonWarAPI.DatabaseAccess.Interfaces;
using DungeonWarAPI.DatabaseAccess;
using DungeonWarAPI.Enum;
using DungeonWarAPI.Models.DAO.Redis;
using DungeonWarAPI.Models.DTO.RequestResponse.Chat;
using Microsoft.AspNetCore.Mvc;

namespace DungeonWarAPI.Controllers.Chat;

[Route("[controller]")]
[ApiController]
public class ChannelChangeController : ControllerBase
{
	private readonly IMemoryDatabase _memoryDatabase;
	private readonly ILogger<ChannelChangeController> _logger;

	public ChannelChangeController(ILogger<ChannelChangeController> logger, IMemoryDatabase memoryDatabase)
	{
		_memoryDatabase = memoryDatabase;
		_logger = logger;
	}

	[HttpPost]
	public async Task<ChannelChangeResponse> Post(ChannelChangeRequest request)
	{
		var userAuthAndState = HttpContext.Items[nameof(UserAuthAndState)] as UserAuthAndState;
		var response = new ChannelChangeResponse();

		var key = MemoryDatabaseKeyGenerator.MakeUIDKey(userAuthAndState.Email);

		var errorCode = await _memoryDatabase.UpdateChatChannelAsync(key, userAuthAndState, request.ChannelNumber);
		if (errorCode != ErrorCode.None)
		{
			response.Error = errorCode;
			return response;
		}

		response.Error = ErrorCode.None;
		return response;
	}
}