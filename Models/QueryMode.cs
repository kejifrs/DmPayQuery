namespace DmPayQuery.Models;

public enum QueryMode
{
    IdRechargeOrGift = 1,    // ID查充值/送礼（取最大值）
    IdGiftOnly = 2,          // ID只查送礼
    UidRechargeOrGift = 3    // UID查充值/送礼
}

public enum DateMode
{
    Original = 1,            // 不调整默认
    PreviousDay = 2          // 拍走日期前1天
}