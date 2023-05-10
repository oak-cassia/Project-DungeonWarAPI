﻿using DungeonWarAPI.ModelConfiguration;
using DungeonWarAPI.Services.Interfaces;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;
using System.Data;
using DungeonWarAPI.Models.Database.Game;
using ZLogger;
using DungeonWarAPI.Models.DTO.Payloads;
using DungeonWarAPI.Enum;

namespace DungeonWarAPI.Services.Implementations;

public class MailService : IMailService
{
	private readonly IOptions<DatabaseConfiguration> _configurationOptions;
	private readonly ILogger<MailService> _logger;
	private readonly OwnedItemFactory _ownedItemFactory;

	private readonly IDbConnection _databaseConnection;
	private readonly QueryFactory _queryFactory;

	public MailService(ILogger<MailService> logger, IOptions<DatabaseConfiguration> configurationOptions,
		OwnedItemFactory ownedItemFactory)
	{
		_configurationOptions = configurationOptions;
		_logger = logger;
		_ownedItemFactory = ownedItemFactory;

		_databaseConnection = new MySqlConnection(configurationOptions.Value.GameDatabase);
		_databaseConnection.Open();

		var compiler = new MySqlCompiler();
		_queryFactory = new QueryFactory(_databaseConnection, compiler);
	}

	public void Dispose()
	{
		_databaseConnection.Dispose();
		//_queryFactory.Dispose();
	}

	public async Task<(ErrorCode, List<MailWithItems>)> LoadMailListAsync(Int32 gameUserId, Int32 pageNumber)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, PageNumber = pageNumber }, "LoadUserMails Start");

		if (pageNumber < 1)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.LoadMailListWrongPage, GameUserId = gameUserId, PageNumber = pageNumber },
				"LoadMailListWrongPage");
			return (ErrorCode.LoadMailListWrongPage, null);
		}

		try
		{
			var mails = await _queryFactory.Query("mail")
				.Where("GameUserId", "=", gameUserId)
				.Where("ExpirationDate", ">", DateTime.Today)
				.Where("isRemoved", "=", false)
				.OrderByDesc("MailId")
				.Limit(Mail.MailCountInPage).Offset((pageNumber - 1) * Mail.MailCountInPage)
				.GetAsync<Mail>();

			var mailsWithItems = new List<MailWithItems>();

			if (!mails.Any())
			{
				_logger.ZLogInformationWithPayload(
					new
					{
						ErrorCode = ErrorCode.LoadMailListEmptyMail,
						GameUserId = gameUserId,
						PageNumber = pageNumber
					},
					"LoadMailListEmptyMail");
				return (ErrorCode.None, mailsWithItems);
			}

			foreach (var mail in mails)
			{
				var (errorCode, items) = await GetMailItemsAsync(gameUserId, mail.MailId);

				if (errorCode != ErrorCode.None)
				{
					return (errorCode, new List<MailWithItems>());
				}

				var a = new List<MailWithItems>();
				Console.WriteLine(a.Count());

				mailsWithItems.Add(new MailWithItems(mail, items));
			}


			return (ErrorCode.None, mailsWithItems.ToList());
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new
				{
					ErrorCode = ErrorCode.LoadMailListFailException,
					GameUserId = gameUserId,
					PageNumber = pageNumber
				},
				"LoadMailListFailException");
			return (ErrorCode.LoadMailListFailException, new List<MailWithItems>());
		}
	}

	public async Task<(ErrorCode, string content)> ReadMailAsync(int gameUserId, long mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId }, "MarkMailAsRead Start");

		try
		{
			var (errorCode, content) = await SelectMailAsync(gameUserId, mailId);
			if (errorCode != ErrorCode.None)
			{
				return (errorCode, "");
			}

			var count = await _queryFactory.Query("mail").Where("MailId", "=", mailId)
				.UpdateAsync(new { IsRead = true });
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(new { }, "");
				return (ErrorCode.ReadMailFailUpdate, "");
			}

			return (ErrorCode.None, content);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.ReadMailFailExceptions, GameUserId = gameUserId, MailId = mailId },
				"ReadMailFailExceptions");
			return (ErrorCode.ReadMailFailExceptions, "");
		}
	}

	public async Task<ErrorCode> MarkMailAsReceiveAsync(Int32 gameUserId, Int64 mailId)
	{
		try
		{
			var mail = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.FirstOrDefaultAsync<Mail>();

			 var errorCode=ValidateUserAndFlagIsReceive(gameUserId, mail.GameUserId, mailId, mail.IsReceived);

			 if (errorCode != ErrorCode.None)
			 {
				 return errorCode;
			 }

			 var count = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.UpdateAsync(new { IsReceived = true });

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.MarkMailAsReceiveFailUpdate,
						GameUserId = gameUserId,
						MailId = mailId
					}, "MarkMailAsReceiveFailUpdate");
				return ErrorCode.MarkMailAsReceiveFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.MarkMailAsReceiveException, GameUserId = gameUserId, MailId = mailId },
				"MarkMailAsReceiveException");
			return ErrorCode.MarkMailAsReceiveException;
		}
	}

	public async Task<ErrorCode> RollbackMarkMailItemAsReceiveAsync(Int32 gameUserId, Int64 mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId },
			"RollbackMarkMailItemAsReceive Start");
		try
		{
			var count = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.UpdateAsync(new { IsReceived = false });


			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackMarkMailItemAsReceiveFailUpdate,
						GameUserId = gameUserId,
						MailId = mailId
					}, "RollbackMarkMailItemAsReceiveFailUpdate");
				return ErrorCode.RollbackMarkMailItemAsReceiveFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new
				{
					ErrorCode = ErrorCode.RollbackMarkMailItemAsReceiveFailException,
					GameUserId = gameUserId,
					MailId = mailId
				},
				"RollbackMarkMailItemAsReceiveFailException");
			return ErrorCode.RollbackMarkMailItemAsReceiveFailException;
		}
	}

	public async Task<ErrorCode> ReceiveItemAsync(Int32 gameUserId, Int64 mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId },
			"ReceiveItemAsync Start");

		var (errorCode, items) = await GetMailItemsAsync(gameUserId, mailId);
		if (errorCode != ErrorCode.None)
		{
			return errorCode;
		}

		if (!items.Any())
		{
			_logger.ZLogErrorWithPayload(new
				{
					ErrorCode = ErrorCode.ReceiveItemFailMailHaveNoItem,
					GameUserId = gameUserId,
					MailId = mailId
				}
				, "ReceiveItemFailMailHaveNoItem");
			return ErrorCode.ReceiveItemFailMailHaveNoItem;
		}


		List<Func<Task>> rollbackActions = new List<Func<Task>>();

		try
		{
			foreach (var item in items)
			{
				if (item.ItemCode == 1)
				{
					errorCode = await IncreaseGoldAsync(gameUserId, item.ItemCount, rollbackActions);
				}
				else if (item.ItemCode == 6)
				{
					errorCode = await IncreasePotionAsync(gameUserId, item.ItemCount, rollbackActions);
				}
				else
				{
					errorCode = await InsertOwnedItemAsync(gameUserId, item.ItemCode, item.ItemCount,
						item.EnhancementCount,
						rollbackActions);
				}

				if (errorCode != ErrorCode.None)
				{
					await RollbackReceiveItemAsync(rollbackActions);
					_logger.ZLogErrorWithPayload(new
							{ ErrorCode = ErrorCode.ReceiveItemFailInsert, GameUserId = gameUserId, MailId = mailId }
						, "ReceiveItemFailInsert");
					return ErrorCode.ReceiveItemFailInsert;
				}
			}


			return ErrorCode.None;
		}
		catch (Exception e)
		{
			await RollbackReceiveItemAsync(rollbackActions);
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.ReceiveItemFailException, GameUserId = gameUserId, MailId = mailId }
				, "ReceiveItemFailException");
			return ErrorCode.ReceiveItemFailException;
		}
	}

	public async Task<ErrorCode> DeleteMailAsync(Int32 gameUserId, Int64 mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId }, "DeleteMail Start");


		try
		{
			var count = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.Where("GameUserId", "=", gameUserId)
				.Where("IsReceived", "=", true)
				.UpdateAsync(new { IsRemoved = true });

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.DeleteMailFailDelete, GameUserId = gameUserId, MailId = mailId },
					"DeleteMailFailDelete");
				return ErrorCode.DeleteMailFailDelete;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.DeleteMailFailException, GameUserId = gameUserId, MailId = mailId },
				"DeleteMailFailException");
			return ErrorCode.DeleteMailFailException;
		}
	}

	private async Task<(ErrorCode, String)> SelectMailAsync(Int32 gameUserId, Int64 mailId)
	{
		var mail = await _queryFactory.Query("mail")
			.Where("MailId", "=", mailId)
			.FirstOrDefaultAsync<Mail>();
		if (mail == null)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.ReadMailFailSelect, GameUserId = gameUserId, MailId = mailId },
				"ReadMailFailSelect");
			return (ErrorCode.ReadMailFailSelect, "");
		}

		if (mail.GameUserId != gameUserId)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.ReadMailFailWrongUser, GameUserId = gameUserId, MailId = mailId },
				"ReadMailFailWrongUser");
			return (ErrorCode.ReadMailFailWrongUser, "");
		}

		return (ErrorCode.None, mail.Contents);
	}


	private async Task<(ErrorCode errorCode, List<MailItem> items)> GetMailItemsAsync(Int32 gameUserId, Int64 mailId)
	{
		try
		{
			var items = await _queryFactory.Query("mail_item")
				.Where("MailId", "=", mailId)
				.GetAsync<MailItem>();

			if (!items.Any())
			{
				return (ErrorCode.None, new List<MailItem>());
			}

			return (ErrorCode.None, items.ToList());
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new
				{
					ErrorCode = ErrorCode.GetMailItemsFailException,
					GameUserId = gameUserId,
					MailId = mailId
				}
				, "GetMailItemsFailException");
			return (ErrorCode.GetMailItemsFailException, null);
		}
	}

	private async Task<ErrorCode> IncreaseGoldAsync(Int32 gameUserId, Int32 itemCount, List<Func<Task>> rollbackActions)
	{
		var count = await _queryFactory.Query("user_data").Where("GameUserId", "=", gameUserId)
			.IncrementAsync("Gold", itemCount);

		if (count != 1)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.IncreaseGoldFailUpdate, GameUserId = gameUserId },
				"IncreaseGoldFailUpdate");
			return ErrorCode.IncreaseGoldFailUpdate;
		}

		rollbackActions.Add(async () =>
		{
			var rollbackCount = await _queryFactory.Query("user_data").Where("GameUserId", "=", gameUserId)
				.DecrementAsync("Gold", itemCount);
			if (rollbackCount != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackIncreaseGoldFail,
						GameUserId = gameUserId,
						ItemCount = itemCount
					}, "RollbackIncreaseGoldFail");
			}
		});
		return ErrorCode.None;
	}


	private async Task<ErrorCode> IncreasePotionAsync(Int32 gameUserId, Int32 itemCount,
		List<Func<Task>> rollbackActions)
	{
		ErrorCode errorCode = ErrorCode.None;
		var count = await _queryFactory.Query("owned_item").Where("GameUserId", "=", gameUserId)
			.Where("ItemCode", "=", (Int32)ItemCode.Potion)
			.IncrementAsync("ItemCount", itemCount);
		if (count == 0)
		{
			errorCode = await InsertOwnedItemAsync(gameUserId, 6, itemCount,
				0, rollbackActions);
		}

		if (errorCode != ErrorCode.None)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.IncreasePotionFailUpdateOrInsert, GameUserId = gameUserId },
				"IncreasePotionFailUpdateOrInsert");
			return ErrorCode.IncreasePotionFailUpdateOrInsert;
		}

		// count == 0 일 때 아이템 생성에 관한 롤백 등록은 InsertOwnedItemAsync에서 진행
		rollbackActions.Add(async () =>
		{
			var rollbackCount = await _queryFactory.Query("owned_item")
				.Where("GameUserId", "=", gameUserId)
				.Where("ItemCode","=",(Int32)ItemCode.Potion)
				.DecrementAsync("ItemCount", itemCount);

			if (rollbackCount != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackIncreasePotionFail,
						GameUserId = gameUserId,
						ItemCount = itemCount
					}, "RollbackIncreasePotionFail");
			}
		});

		return ErrorCode.None;
	}

	private async Task<ErrorCode> InsertOwnedItemAsync(Int32 gameUserId, Int32 itemCode, Int32 itemCount,
		Int32 enhancementCount, List<Func<Task>> rollbackActions)
	{
		var itemId = await _queryFactory.Query("owned_item")
			.InsertGetIdAsync<int>(
				_ownedItemFactory.CreateOwnedItem(gameUserId, itemCode, enhancementCount, itemCount));

		if (itemId == 0)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.InsertOwnedItemFailInsert, GameUserId = gameUserId },
				"InsertOwnedItemFailInsert");
			return ErrorCode.InsertOwnedItemFailInsert;
		}

		rollbackActions.Add(async () =>
		{
			var rollbackCount = await _queryFactory.Query("user_data").Where("ItemId", "=", itemId)
				.DeleteAsync();

			if (rollbackCount != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackInsertOwnedItemFail,
						GameUserId = gameUserId,
						ItemId = itemId
					}, "RollbackInsertOwnedItemFail");
			}
		});

		return ErrorCode.None;
	}

	private async Task RollbackReceiveItemAsync(List<Func<Task>> rollbackActions)
	{
		foreach (var action in rollbackActions)
		{
			await action();
		}
	}

	private ErrorCode ValidateUserAndFlagIsReceive(Int32 gameUserId, Int32 mailOwnerId ,Int64 mailId,Boolean isReceived)
	{
		if (mailOwnerId != gameUserId)
		{
			_logger.ZLogErrorWithPayload(new
				{
					ErrorCode = ErrorCode.MarkMailAsReceiveFailWrongGameUserId,
					GameUserId = gameUserId,
					MailId = mailId,
				},
				"MarkMailAsReceiveFailWrongGameUserId");
			return ErrorCode.MarkMailAsReceiveFailWrongGameUserId;
		}

		if (isReceived == true)
		{
			_logger.ZLogErrorWithPayload(new
			{
				ErrorCode = ErrorCode.MarkMailAsReceiveFailAlreadyReceived,
				GameUserId = gameUserId,
				MailId = mailId
			}, "MarkMailAsReceiveFailAlreadyReceived");
			return ErrorCode.MarkMailAsReceiveFailAlreadyReceived;
		}

		return ErrorCode.None;
	}
}