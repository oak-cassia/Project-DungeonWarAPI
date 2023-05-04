﻿namespace DungeonWarAPI;

public enum ErrorCode : int
{
	None=0,

	LoadAttendanceRewardFailException =10,
	LoadAttendanceRewardsFailSelect =11,
	LoadItemsFailException =20,
	LoadItemsFailSelect = 21,
	LoadItemAttributeFailException =30,
	LoadItemAttributeFailSelect = 31,
	LoadPackageItemFailException =40,
	LoadPackageItemFailSelect = 41,
	LoadShopPackagesFailException =50,
	LoadShopPackagesFailSelect = 51,
	LoadStageItemsFailException =60,
	LoadStageItemsFailSelect = 61,
	LoadStageNpcsFailException =70,
	LoadStageNpcsFailSelect = 71,

	CreateAccountFailException = 110,
	CreateAccountFailDuplicate =111,
	CreateAccountFailInsert =112,

	CreateUserFailException=120,
	CreateUserFailDuplicate =121,
	CreateUserFailInsert = 122,
	RollbackAccountFailException=126,
	RollbackAccountFailDelete=127,

	CreateUserItemFailException=140,
	CreateUserItemFailInsert = 141,
	RollbackCreateUserDataFailException =146,
	RollbackCreateUserDataFailDelete=147,

	RollbackMarkMailItemAsReceiveFailException=150,
	RollbackMarkMailItemAsReceiveFailUpdate=151,

	AddNewItemAndGetIdFailException = 200,

	LoginFailException = 220,
	LoginFailNotUser = 221,
	LoginFailWrongPassword = 222,
	LoginFailUserNotExist =223,
	LoginFailRegisterToRedis = 224,
	LoginFailRegisterToRedisException =225,

	LoadUserDataFailException = 230,
	LoadUserDataFailSelect= 231,

	LoadUserItemsFailException=236,
	LoadUserItemsFailSelect=237,

	LoadNotificationsFailException =250,
	LoadNotificationsZeroNotification=251,

	LoadMailListFailException =260,
	LoadMailListEmptyMail=261,
	LoadMailListWrongPage=262,

	StoreUserMailPageFailException= 270,
	StoreUserMailPageFailSet=271,

	VerifyMailOwnerIdFailException = 280,
	VerifyMailOwnerIdFailWrongId= 281,
	MarkMailAsReadFailUpdate=285,
	MarkMailAsReadFailExceptions = 286,

	MarkMailItemAsReceiveException=290,
	MarkMailItemAsReceiveFailUpdate=291,
	MarkMailItemAsReceiveFailSelect= 292,
	MarkMailItemAsReceiveFailAlreadyReceived= 293,

	ReceiveItemFailException =300,
	ReceiveItemFailInsert= 301,
	ReceiveItemFailMailHaveNoItem=302,
	GetMailItemsFailException=303,
	IncreaseGoldFailUpdate = 304,
	IncreasePotionFailUpdateOrInsert = 305,
	InsertOwnedItemFailInsert=306,
	RollbackIncreaseGoldFail =307,
	RollbackIncreasePotionFail=308,
	RollbackInsertOwnedItemFail=309,
	
	DeleteMailFailException = 310,
	DeleteMailFailDelete = 311,
	DeleteMailFail=311,

	UpdateLoginDateFailException = 320,
	UpdateLoginDateFailUserNotFound= 321,
	UpdateLoginDateFailUpdate=322,
	UpdateLoginDateFailAlreadyReceived=323,

	CreateAttendanceMailFailException=330,
	CreateAttendanceMailFailInsertMail=331,
	CreateAttendanceMailFailInsertItem=332,

	RollbackCreateMailFailException =335,
	RollbackCreateMailFailDelete=336,


	InvalidRequestHttpBody =500,
	WrongAppVersion=501,
	WrongMasterDataVersion=502,
	WrongAuthTokenRequest=503,

	LoadAuthUserDataFailException = 510,
	LoadAuthUserDataFailEmpty = 511,


	LockUserRequestFailExceptions=1000,
	LockUserRequestFailISet=1001,

	UnLockUserRequestFailException=1010,
	UnLockUserRequestFailNullKey=1011,
	UnLockUserRequestFailDelete=1012,





}