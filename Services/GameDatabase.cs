﻿using DungeonWarAPI.ModelConfiguration;
using Humanizer;
using Microsoft.Extensions.Options;
using MySqlConnector;
using SqlKata.Compilers;
using SqlKata.Execution;
using System.Data;
using ZLogger;
using DungeonWarAPI.Models.Database.Game;
using System.Threading;
using System.Transactions;
using DungeonWarAPI.Models.DTO;
using DungeonWarAPI.Models.DAO.Game;

namespace DungeonWarAPI.Services;

public class GameDatabase : IGameDatabase
{
	private readonly IOptions<DatabaseConfiguration> _configurationOptions;
	private readonly ILogger<GameDatabase> _logger;
	private readonly MasterDataManager _masterData;

	private readonly IDbConnection _databaseConnection;
	private readonly QueryFactory _queryFactory;

	public GameDatabase(ILogger<GameDatabase> logger, IOptions<DatabaseConfiguration> configurationOptions,
		MasterDataManager masterData)
	{
		_configurationOptions = configurationOptions;
		_logger = logger;
		_masterData = masterData;

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

	public async Task<(ErrorCode, Int32 )> CreateUserAsync(int playerId)
	{
		try
		{
			_logger.ZLogDebugWithPayload(new { PlayerId = playerId }, "CreateUser Start");

			var gameUserId = await _queryFactory.Query("user_data")
				.InsertGetIdAsync<Int32>(new { PlayerId = playerId });

			_logger.ZLogInformationWithPayload(new { GameUserId = gameUserId }, "CreateUser Success");

			return (ErrorCode.None, gameUserId);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.CreateUserFailException },
				"CreateUserFailException");
			return (ErrorCode.CreateUserFailException, 0);
		}
	}

	public async Task<ErrorCode> CreateUserItemAsync(Int32 gameUserId)
	{
		try
		{
			_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "CreateUserItem Start");
			var columns = new[] { "GameUserId", "ItemCode", "EnhancementCount", "ItemCount" };
			var data = new[]
			{
				new object[] { gameUserId, 2, 0, 1 },
				new object[] { gameUserId, 3, 0, 1 }
			};

			var count = await _queryFactory.Query("owned_item").InsertAsync(columns, data);

			if (count < 1)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.CreateUserItemFailInsert, GameUserId = gameUserId },
					"CreateUserItemFailInsert");
				return ErrorCode.CreateUserItemFailInsert;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.CreateUserItemFailException, GameUserId = gameUserId },
				"CreateUserItemFailException");
			return ErrorCode.CreateUserItemFailException;
		}
	}

	public async Task<ErrorCode> RollbackCreateUserAsync(Int32 gameUserId)
	{
		try
		{
			_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "RollbackUser Start");

			var count = await _queryFactory.Query("user_data")
				.Where("GameUserId", "=", gameUserId).DeleteAsync();

			if (count < 1)
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.RollbackCreateUserDataFailDelete },
					"RollbackCreateUserDataFailDelete");
				return ErrorCode.RollbackCreateUserDataFailDelete;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.RollbackCreateUserDataFailException },
				"RollbackCreateUserDataFailException");
			return ErrorCode.RollbackCreateUserDataFailException;
		}
	}

	public async Task<(ErrorCode, UserData)> LoadUserDataAsync(int playerId)
	{
		_logger.ZLogDebugWithPayload(new { PlayerId = playerId }, "LoadUserData Start");

		try
		{
			var userData = await _queryFactory.Query("user_data")
				.Where("PlayerId", "=", playerId).FirstOrDefaultAsync<UserData>();
			if (userData == null)
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.LoadUserDataFailSelect, PlayerId = playerId },
					"ErrorCode.LoadUserDataFailSelect");
				return (ErrorCode.LoadUserDataFailSelect, null);
			}

			return (ErrorCode.None, userData);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.LoadUserDataFailException, PlayerId = playerId },
				"LoadUserDataFailException");
			return (ErrorCode.LoadUserDataFailException, null);
		}
	}

	public async Task<(ErrorCode, List<OwnedItem> )> LoadUserItemsAsync(Int32 gameUserId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "LoadUserItems Start");
		try
		{
			var items = await _queryFactory.Query("owned_item").Where("GameUserId", "=", gameUserId)
				.GetAsync<OwnedItem>();

			if (!items.Any())
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.LoadUserItemsFailSelect, GameUserId = gameUserId },
					"LoadUserItemsFailSelect");
				return (ErrorCode.LoadUserItemsFailSelect, null);
			}

			return (ErrorCode.None, items.ToList());
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.LoadUserItemsFailException, GameUserId = gameUserId },
				"LoadUserItemsFailException");
			return (ErrorCode.LoadUserItemsFailException, null);
		}
	}

	public async Task<(ErrorCode, List<MailWithItems>)> LoadMailListAsync(int gameUserId, int pageNumber)
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

			if (!mails.Any())
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.LoadMailListEmptyMail, GameUserId = gameUserId, PageNumber = pageNumber
					},
					"LoadMailListEmptyMail");
				return (ErrorCode.LoadMailListEmptyMail, null);
			}

			var mailsWithItems = new List<MailWithItems>();

			foreach (var mail in mails)
			{
				var (errorCode, items) = await GetMailItemsAsync(gameUserId, mail.MailId);

				if (errorCode != ErrorCode.None)
				{
					return (errorCode, null);
				}

				mailsWithItems.Add(new MailWithItems { Mail = mail, Items = items });
			}


			return (ErrorCode.None, mailsWithItems.ToList());
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.LoadMailListFailException, GameUserId = gameUserId, PageNumber = pageNumber
				},
				"LoadMailListFailException");
			return (ErrorCode.LoadMailListFailException, null);
		}
	}

	public async Task<ErrorCode> MarkMailAsReadAsync(Int32 gameUserId, Int64 mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId }, "MarkMailAsRead Start");

		try
		{
			var count = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				//.Where("GameUserId","=",gameUserId)
				.UpdateAsync(new { IsRead = true });
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.MarkMailAsReadFailUpdate, GameUserId = gameUserId, MailId = mailId },
					"MarkMailAsReadFailUpdate");
				return ErrorCode.MarkMailAsReadFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.MarkMailAsReadFailExceptions, GameUserId = gameUserId, MailId = mailId },
				"MarkMailAsReadFailExceptions");
			return ErrorCode.MarkMailAsReadFailExceptions;
		}
	}

	public async Task<ErrorCode> MarkMailItemAsReceiveAsync(Int32 gameUserId, Int64 mailId)
	{
		try
		{
			bool isReceived = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.Where("GameUserId", "=", gameUserId)
				.Select("IsReceived")
				.FirstOrDefaultAsync<bool>();

			if (isReceived == true)
			{
				_logger.ZLogErrorWithPayload(new
				{
					ErrorCode = ErrorCode.MarkMailItemAsReceiveFailAlreadyReceived,
					GameUserId = gameUserId, MailId = mailId
				}, "MarkMailItemAsReceiveFailAlreadyReceived");
				return (ErrorCode.MarkMailItemAsReceiveFailAlreadyReceived);
			}


			var count = await _queryFactory.Query("mail")
				.Where("MailId", "=", mailId)
				.UpdateAsync(new { IsReceived = true });
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.MarkMailItemAsReceiveFailUpdate, GameUserId = gameUserId, MailId = mailId
					}, "MarkMailItemAsReceiveFailUpdate");
				return (ErrorCode.MarkMailItemAsReceiveFailUpdate);
			}

			return (ErrorCode.None);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.MarkMailItemAsReceiveException, GameUserId = gameUserId, MailId = mailId },
				"MarkMailItemAsReceiveException");
			return (ErrorCode.MarkMailItemAsReceiveException);
		}
	}

	public async Task<ErrorCode> RollbackMarkMailItemAsReceiveAsync(int gameUserId, long mailId)
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
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.RollbackMarkMailItemAsReceiveFailException, GameUserId = gameUserId,
					MailId = mailId
				},
				"RollbackMarkMailItemAsReceiveFailException");
			return ErrorCode.RollbackMarkMailItemAsReceiveFailException;
		}
	}

	public async Task<ErrorCode> ReceiveItemAsync(int gameUserId, Int64 mailId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, MailId = mailId },
			"ReceiveItemAsync Start");

		var (errorCode, items) = await GetMailItemsAsync(gameUserId, mailId);

		if (!items.Any())
		{
			_logger.ZLogErrorWithPayload(new
				{
					ErrorCode = ErrorCode.ReceiveItemFailMailHaveNoItem,
					GameUserId = gameUserId,
					MailId = mailId
				}
				, "ReceiveItemFailMailHaveNoItem");
			return (ErrorCode.ReceiveItemFailMailHaveNoItem);
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
			_logger.ZLogErrorWithPayload(new
					{ ErrorCode = ErrorCode.ReceiveItemFailException, GameUserId = gameUserId, MailId = mailId }
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
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.DeleteMailFailException, GameUserId = gameUserId, MailId = mailId },
				"DeleteMailFailException");
			return ErrorCode.DeleteMailFailException;
		}
	}

	public async Task<(ErrorCode, DateTime lastLoginDate, short attendanceCount)> UpdateLoginDateAsync(
		Int32 gameUserId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "UpdateLoginAndGetAttendance");

		try
		{
			var userData = await _queryFactory.Query("user_data")
				.Where("GameUserId", "=", gameUserId).FirstOrDefaultAsync<UserData>();

			if (userData == null)
			{
				_logger.ZLogErrorWithPayload(
					new { GameUserId = gameUserId, ErrorCode = ErrorCode.UpdateLoginDateFailUserNotFound },
					"UpdateLoginDateFailUserNotFound");
				return (ErrorCode.UpdateLoginDateFailUserNotFound, default, default);
			}

			var lastLoginDate = userData.LastLoginDate.Date;
			var today = DateTime.Now.Date;

			Int16 attendanceCount = userData.AttendanceCount;

			if (lastLoginDate == today)
			{
				_logger.ZLogErrorWithPayload(
					new { GameUserId = gameUserId, ErrorCode = ErrorCode.UpdateLoginDateFailAlreadyReceived },
					"UpdateLoginDateFailAlreadyReceived");
				return (ErrorCode.UpdateLoginDateFailAlreadyReceived, default, default);
			}

			if (lastLoginDate == today.AddDays(-1) && lastLoginDate.Date.Month == today.Month)
			{
				attendanceCount++;
			}
			else
			{
				attendanceCount = 1;
			}


			var count = await _queryFactory.Query("user_data")
				.Where("GameUserId", "=", gameUserId)
				.UpdateAsync(new { LastLoginDate = today, AttendanceCount = attendanceCount });

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new { GameUserId = gameUserId, ErrorCode = ErrorCode.UpdateLoginDateFailUpdate },
					"UpdateLoginDateFailUpdate");
				return (ErrorCode.UpdateLoginDateFailUpdate, default, default);
			}

			return (ErrorCode.None, lastLoginDate, attendanceCount);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { GameUserId = gameUserId, ErrorCode = ErrorCode.UpdateLoginDateFailException },
				"UpdateLoginDateFailException");
			return (ErrorCode.UpdateLoginDateFailException, default, default);
		}
	}

	public async Task<ErrorCode> CreateAttendanceRewardMailAsync(Int32 gameUserId, AttendanceReward reward)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "CreateAttendanceMail Start");
		Int64 mailId = 0;
		try
		{
			mailId = await _queryFactory.Query("mail").InsertGetIdAsync<Int64>(
				new
				{
					GameUserId = gameUserId,
					Title = reward.AttendanceCount.ToString() + "일 출석 보상",
					Contents = reward.AttendanceCount.ToString() + "일 출석 보상 지급 안내",
					isRead = false,
					isReceived = false,
					isInApp = false,
					isRemoved = false,
				});
			if (mailId < 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.CreateAttendanceMailFailInsertMail, GameUserId = gameUserId,
						MailId = mailId
					},
					"CreateAttendanceMailFailInsertMail");
				return ErrorCode.CreateAttendanceMailFailInsertMail;
			}

			var count = await _queryFactory.Query("mail_item").InsertAsync(new
			{
				MailId = mailId,
				ItemCode = reward.ItemCode,
				ItemCount = reward.ItemCount,
			});

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.CreateAttendanceMailFailInsertItem,
						GameUserId = gameUserId,
						MailId = mailId
					},
					"CreateAttendanceMailFailInsertItem");

				await RollbackCreateMailAsync(mailId);

				return ErrorCode.CreateAttendanceMailFailInsertItem;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.CreateAttendanceMailFailException,
					GameUserId = gameUserId,
					MailId = mailId
				},
				"CreateAttendanceMailFailException");
			await RollbackCreateMailAsync(mailId);
			return ErrorCode.CreateAttendanceMailFailException;
		}
	}

	public async Task<ErrorCode> RollbackLoginDateAsync(int gameUserId, DateTime lastLoginDate, short attendanceCount)
	{
		_logger.ZLogDebugWithPayload(
			new { GameUserId = gameUserId, LastLogin = lastLoginDate, AttendanceCount = attendanceCount },
			"RollbackLoginDate Start");
		try
		{
			var count = await _queryFactory.Query("user_data")
				.Where("GameUserId", "=", gameUserId)
				.UpdateAsync(new { LastLoginDate = lastLoginDate, AttendanceCount = attendanceCount - 1 });

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackLoginDateFailUpdate,
						LastLoginDate = lastLoginDate,
						AttendnceCount = attendanceCount
					},
					"RollbackLoginDateFailUpdate");
				return ErrorCode.RollbackLoginDateFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.RollbackLoginDateFailException,
					LastLoginDate = lastLoginDate,
					AttendnceCount = attendanceCount
				},
				"RollbackLoginDateFailException");
			return ErrorCode.RollbackLoginDateFailException;
		}
	}

	public async Task<(ErrorCode, Int32)> StoreReceiptAsync(Int32 gameUserId, String receiptSerialCode, Int32 packageId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, ReceiptSerialCode = receiptSerialCode },
			"StoreReceipt Start");

		try
		{
			var existingReceipt = await _queryFactory.Query("receipt")
				.Where("ReceiptSerialCode", receiptSerialCode)
				.FirstOrDefaultAsync();

			if (existingReceipt != null)
			{
				_logger.ZLogErrorWithPayload(new
					{
						ErrorCode = ErrorCode.StoreReceiptFailDuplicatedReceipt, GameUserId = gameUserId,
						ReceiptSerialCode = receiptSerialCode
					}
					, "StoreReceiptFailDuplicatedReceipt");
				return (ErrorCode.StoreReceiptFailDuplicatedReceipt, 0);
			}

			var receiptId = await _queryFactory.Query("receipt")
				.InsertGetIdAsync<int>(new
				{
					GameUserId = gameUserId,
					ReceiptSerialCode = receiptSerialCode,
					PurchaseDate = DateTime.UtcNow,
					PackageId = packageId
				});

			if (receiptId < 1)
			{
				_logger.ZLogErrorWithPayload(new
					{
						ErrorCode = ErrorCode.StoreReceiptFailInsert, GameUserId = gameUserId,
						ReceiptSerialCode = receiptSerialCode
					}
					, "StoreReceiptFailInsert");
				return (ErrorCode.StoreReceiptFailInsert, 0);
			}

			return (ErrorCode.None, receiptId);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new
				{
					ErrorCode = ErrorCode.StoreReceiptFailException, GameUserId = gameUserId,
					ReceiptSerialCode = receiptSerialCode
				}
				, "StoreReceiptFailException");
			return (ErrorCode.StoreReceiptFailException, 0);
		}
	}

	public async Task<ErrorCode> CreateInAppMailAsync(int gameUserId, List<PackageItem> packageItems)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId }, "CreateInAppMail Start");
		Int64 mailId = 0;
		try
		{
			mailId = await _queryFactory.Query("mail").InsertGetIdAsync<Int64>(
				new
				{
					GameUserId = gameUserId,
					Title = packageItems[0].PackageId + "구매 지급",
					Contents = packageItems[0].PackageId + "구매 지급 안내",
					isRead = false,
					isReceived = false,
					isInApp = true,
					isRemoved = false,
				});
			if (mailId < 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.CreateInAppMailFailInsertMail,
						GameUserId = gameUserId,
						MailId = mailId
					},
					"CreateInAppMailFailInsertMail");
				return ErrorCode.CreateInAppMailFailInsertMail;
			}

			var mailItemsData = packageItems.Select(item => new object[] { mailId, item.ItemCode, item.ItemCount })
				.ToArray();
			var mailItemsColumns = new[] { "MailId", "ItemCode", "ItemCount" };

			var count = await _queryFactory.Query("mail_item").InsertAsync(mailItemsColumns, mailItemsData);


			if (count < 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.CreateInAppMailFailInsertItem,
						GameUserId = gameUserId,
						MailId = mailId
					},
					"CreateInAppMailFailInsertItem");

				await RollbackCreateMailAsync(mailId);

				return ErrorCode.CreateInAppMailFailInsertItem;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.CreateInAppMailFailException,
					GameUserId = gameUserId,
					MailId = mailId
				},
				"CreateInAppMailFailException");
			await RollbackCreateMailAsync(mailId);
			return ErrorCode.CreateInAppMailFailException;
		}
	}

	public async Task<ErrorCode> RollbackStoreReceiptAsync(Int32 receiptId)
	{
		if (receiptId == 0)
		{
			return ErrorCode.RollbackStoreReceiptFailWrongId;
		}

		try
		{
			var count = await _queryFactory.Query("receipt").Where("ReceiptId", "=", receiptId).DeleteAsync();

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.RollbackStoreReceiptFailDelete, Receipt = receiptId },
					"RollbackStoreReceiptFailDelete");
				return ErrorCode.RollbackStoreReceiptFailDelete;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.RollbackStoreReceiptFailException, Receipt = receiptId },
				"RollbackStoreReceiptFailException");
			return ErrorCode.RollbackStoreReceiptFailException;
		}
	}

	public async Task<(ErrorCode, Int32 itemCode, Int32 enhancementCount)> LoadItemAsync(Int32 gameUserId, Int64 itemId)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, ItemId = itemId }, "LoadItem Start");
		try
		{
			var item = await _queryFactory.Query("owned_item").Where("GameUserId", "=", gameUserId)
				.Where("ItemId", "=", itemId)
				.FirstOrDefaultAsync<OwnedItem>();

			if (item == null)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.LoadItemFailSelect, GameUserId = gameUserId, ItemId = itemId },
					"LoadItemFailSelect");
				return (ErrorCode.LoadItemFailSelect, 0, 0);
			}

			return (ErrorCode.None, item.ItemCode, item.EnhancementCount);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new { ErrorCode = ErrorCode.LoadItemFailException, GameUserId = gameUserId, ItemId = itemId },
				"LoadItemFailException");
			return (ErrorCode.LoadItemFailException, 0, 0);
		}
	}

	public async Task<ErrorCode> UpdateGoldAsync(Int32 gameUserId, Int32 gold)
	{
		_logger.ZLogDebugWithPayload(new { GameUserId = gameUserId, Gold = gold }, "UpdateGold Start");

		try
		{
			var count = await _queryFactory.Query("user_data").Where("GameUserId", "=", gameUserId)
				.IncrementAsync("Gold", gold);
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(new {ErrorCode= ErrorCode.UpdateGoldFailIncrease, GameUserId = gameUserId, Gold = gold }, "UpdateGoldFailIncrease");
				return ErrorCode.UpdateGoldFailIncrease;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.UpdateGoldFailException, GameUserId = gameUserId, Gold = gold }, "UpdateGoldFailException");
			return ErrorCode.UpdateGoldFailException;
		}
	}

	public async Task<ErrorCode> UpdateEnhancementCountAsync(Int32 gameUserId, Int64 itemId, Int32 enhancementCount)
	{
		_logger.ZLogDebugWithPayload(
			new { GameUserId = gameUserId, ItemId = itemId, EnhancementCount = enhancementCount }, "UpdateEnhancementCount Start");

		try
		{
			var count = await _queryFactory.Query("owned_item").Where("ItemId", "=", itemId)
				.UpdateAsync(new { EnhancementCount = enhancementCount + 1 });
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.UpdateEnhancementCountFailUpdate, GameUserId = gameUserId,
						ItemId = itemId, EnhancementCount = enhancementCount
					}, "UpdateEnhancementCountFailUpdate");
				return ErrorCode.UpdateEnhancementCountFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.UpdateEnhancementCountFailException, GameUserId = gameUserId, ItemId = itemId,
					EnhancementCount = enhancementCount
				}, "UpdateEnhancementCountFailException");
			return ErrorCode.UpdateEnhancementCountFailException;
		}
	}

	public async Task<ErrorCode> InsertEnhancementHistoryAsync(Int32 gameUserId, Int64 itemId, Int32 enhancementCount,
		Boolean isSuccess)
	{
		_logger.ZLogDebugWithPayload(new{GameUserId=gameUserId, ItemId=itemId,EnhancementCount= enhancementCount},"InsertEnhancementHistory Start");
		
		var enhancementCountAfter = enhancementCount;
		if (isSuccess)
		{
			enhancementCountAfter = enhancementCount + 1;
		}

		try
		{
			var count = await _queryFactory.Query("enhancement_history").InsertAsync(new
			{
				GameUserId = gameUserId, ItemId = itemId,
				EnhancementCountBefore = enhancementCount,
				EnhancementCountAfter = enhancementCountAfter,
				IsSuccess = isSuccess
			});

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(new{
					ErrorCode=ErrorCode.InsertEnhancementHistoryFailInsert,
					GameUserId = gameUserId,
					ItemId = itemId,
					EnhancementCountBefore = enhancementCount,
					EnhancementCountAfter = enhancementCountAfter,
					IsSuccess = isSuccess
				}, "InsertEnhancementHistoryFailInsert");
				return ErrorCode.InsertEnhancementHistoryFailInsert;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new {
				ErrorCode = ErrorCode.InsertEnhancementHistoryFailException,
				GameUserId = gameUserId,
				ItemId = itemId,
				EnhancementCountBefore = enhancementCount,
				EnhancementCountAfter = enhancementCountAfter,
				IsSuccess = isSuccess
			}, "InsertEnhancementHistoryFailException");
			return ErrorCode.InsertEnhancementHistoryFailException;
		}
	}

	public async Task<ErrorCode> RollbackUpdateMoneyAsync(int gameUserId, int gold)
	{
		return await UpdateGoldAsync(gameUserId, -gold);
	}


	public async Task<ErrorCode> RollbackUpdateEnhancementCountAsync(long itemId)
	{
		_logger.ZLogDebugWithPayload(
			new {  ItemId = itemId }, "RollbackUpdateEnhancementCount Start");

		try
		{
			var count = await _queryFactory.Query("owned_item").Where("ItemId", "=", itemId)
				.DecrementAsync("EnhancementCount",1);
			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new
					{
						ErrorCode = ErrorCode.RollbackUpdateEnhancementCountFailUpdate,
			
						ItemId = itemId,
					}, "RollbackUpdateEnhancementCountFailUpdate");
				return ErrorCode.RollbackUpdateEnhancementCountFailUpdate;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(
				new
				{
					ErrorCode = ErrorCode.RollbackUpdateEnhancementCountFailException,
					ItemId = itemId,
				}, "RollbackUpdateEnhancementCountFailException");
			return ErrorCode.RollbackUpdateEnhancementCountFailException;
		}
	}

	private async Task<(ErrorCode errorCode, List<MailItem> items)> GetMailItemsAsync(int gameUserId, Int64 mailId)
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
			_logger.ZLogErrorWithPayload(new
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
			.Where("ItemCode", "=", 6)
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

		rollbackActions.Add(async () =>
		{
			var rollbackCount = await _queryFactory.Query("owned_item").Where("GameUserId", "=", gameUserId)
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
		var itemId = await _queryFactory.Query("owned_item").InsertGetIdAsync<Int32>(new
		{
			GameUserId = gameUserId,
			ItemCode = itemCode,
			ItemCount = itemCount,
			EnhancementCount = enhancementCount
		});

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
					.DeleteAsync()
				;
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

	private async Task RollbackCreateMailAsync(Int64 mailId)
	{
		if (mailId == 0)
		{
			return;
		}

		try
		{
			var count = await _queryFactory.Query("mail").Where("MailId", "=", mailId).DeleteAsync();

			if (count != 1)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.RollbackCreateMailFailDelete, MailId = mailId },
					"RollbackCreateMailFailDelete");
			}
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.RollbackCreateMailFailException, MailId = mailId },
				"RollbackCreateMailFailException");
		}
	}
}