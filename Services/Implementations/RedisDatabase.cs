﻿using CloudStructures;
using CloudStructures.Structures;
using DungeonWarAPI.Enum;
using DungeonWarAPI.ModelConfiguration;
using DungeonWarAPI.Models.DAO.Account;
using DungeonWarAPI.Models.Database.Game;
using DungeonWarAPI.Services.Interfaces;
using DungeonWarAPI.Utilities;
using Microsoft.Extensions.Options;
using ZLogger;

namespace DungeonWarAPI.Services.Implementations;

public class RedisDatabase : IMemoryDatabase
{
	public RedisConnection _redisConnection;
	private ILogger<RedisDatabase> _logger;

	public RedisDatabase(IOptions<DatabaseConfiguration> configuration, ILogger<RedisDatabase> logger)
	{
		var config = new RedisConfig("default", configuration.Value.Redis);
		_redisConnection = new RedisConnection(config);
		_logger = logger;
	}

	public async Task<ErrorCode> RegisterUserAsync(String email, String authToken, UserData uesrData)
	{
		_logger.ZLogDebugWithPayload(new { Email = email }, "RegisterUser Start");

		var key = MemoryDatabaseKeyUtility.MakeUIDKey(email);
		var authInfo = new AuthUserData
		{
			Email = email,
			AuthToken = authToken,
			GameUserId = uesrData.GameUserId,
			PlayerId = uesrData.PlayerId,
		};

		try
		{
			var redis = new RedisString<AuthUserData>(_redisConnection, key, TimeSpan.FromMinutes(60));
			if (await redis.SetAsync(authInfo) == false)
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.LoginFailRegisterToRedis, Email = email },
					"RegisterUser Fail");
				return ErrorCode.LoginFailRegisterToRedis;
			}
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.LoginFailRegisterToRedisException, Email = email },
				"RegisterUser Exception");
			return ErrorCode.LoginFailRegisterToRedisException;
		}


		return ErrorCode.None;
	}

	public async Task<(ErrorCode, List<String>)> LoadNotificationsAsync()
	{
		_logger.ZLogDebugWithPayload(new { }, "LoadNotifications Start");

		try
		{
			var redis = new RedisSet<String>(_redisConnection, "Notifications", null);

			var notifications = await redis.MembersAsync();

			if (!notifications.Any())
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.LoadNotificationsZeroNotification },
					"LoadNotificationsZeroNotification");
				return (ErrorCode.None, new List<String> { "공지 없음" });
			}


			return (ErrorCode.None, notifications.ToList());
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e, new { ErrorCode = ErrorCode.LoadNotificationsFailException },
				"LoadNotificationsException");
			return (ErrorCode.LoadNotificationsFailException, null);
		}
	}

	public async Task<(ErrorCode, AuthUserData)> LoadAuthUserDataAsync(String email)
	{
		var key = MemoryDatabaseKeyUtility.MakeUIDKey(email);
		_logger.ZLogDebugWithPayload(new { Email = email, Key = key }, "LoadAuthUserData Start");
		try
		{
			var redis = new RedisString<AuthUserData>(_redisConnection, key, null);
			var userData = await redis.GetAsync();
			if (!userData.HasValue)
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.LoadAuthUserDataFailEmpty, Key = key },
					"LoadAuthUserDataFailEmpty");
				return (ErrorCode.LoadAuthUserDataFailEmpty, null);
			}

			return (ErrorCode.None, userData.Value);
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e, new { ErrorCode = ErrorCode.LoadAuthUserDataFailException, Key = key },
				"LoadAuthUserDataFailException");
			return (ErrorCode.LoadAuthUserDataFailException, null);
		}
	}

	public async Task<ErrorCode> LockUserRequestAsync(String key, String authToken)
	{
		_logger.ZLogDebugWithPayload(new { Key = key }, "SetUserRequestLock Start");
		try
		{
			var redis = new RedisString<String>(_redisConnection, key, TimeSpan.FromSeconds(3));

			if (await redis.SetAsync(authToken, TimeSpan.FromSeconds(3),
				    StackExchange.Redis.When.NotExists) == false)
			{
				_logger.ZLogErrorWithPayload(new { ErrroCode = ErrorCode.LockUserRequestFailISet },
					"LockUserRequestFailISet");
				return ErrorCode.LockUserRequestFailISet;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e, new { ErrroCode = ErrorCode.LockUserRequestFailExceptions },
				"LockUserRequestFailExceptions");
			return ErrorCode.LockUserRequestFailExceptions;
		}
	}

	public async Task<ErrorCode> UnLockUserRequestAsync(String key)
	{
		_logger.ZLogDebugWithPayload(new { }, "UnLockUserRequestAsync Start");

		if (String.IsNullOrEmpty(key))
		{
			return ErrorCode.UnLockUserRequestFailNullKey;
		}

		try
		{
			var redis = new RedisString<AuthUserData>(_redisConnection, key, null);
			if (await redis.DeleteAsync() == false)
			{
				_logger.ZLogErrorWithPayload(new { ErrorCode = ErrorCode.UnLockUserRequestFailDelete, Key = key },
					"UnLockUserRequestFailDelete");
				return ErrorCode.UnLockUserRequestFailDelete;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e, new { ErrorCode = ErrorCode.UnLockUserRequestFailException, Key = key },
				"UnLockUserRequestFailExceptions");
			return ErrorCode.UnLockUserRequestFailException;
		}
	}

	public async Task<ErrorCode> StoreUserMailPageAsync(AuthUserData authUserData, Int32 pageNumber)
	{
		_logger.ZLogDebugWithPayload(new { authUserData.GameUserId, PageNumber = pageNumber },
			"StoreUserMailPage Start");
		var key = MemoryDatabaseKeyUtility.MakeMailPageKey(authUserData.Email);
		try
		{
			var redis = new RedisString<Int32>(_redisConnection, key, TimeSpan.FromHours(2));

			if (await redis.SetAsync(pageNumber) == false)
			{
				_logger.ZLogErrorWithPayload(
					new { ErrorCode = ErrorCode.StoreUserMailPageFailSet, authUserData.GameUserId },
					"StoreUserMailPageFailSet");
				return ErrorCode.StoreUserMailPageFailSet;
			}

			return ErrorCode.None;
		}
		catch (Exception e)
		{
			_logger.ZLogErrorWithPayload(e,
				new { ErrorCode = ErrorCode.StoreUserMailPageFailException, authUserData.GameUserId },
				"StoreUserMailPageFailException");
			return ErrorCode.StoreUserMailPageFailException;
		}
	}
}