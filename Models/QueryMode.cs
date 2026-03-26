namespace DmPayQuery.Models;

public enum QueryMode
{
    IdRechargeOrGift = 1,           // ID查充值/送礼（取最大值）
    IdGiftOnly = 2,                  // ID只查送礼
    UidRechargeOrGift = 3,           // UID查充值/送礼
    RoomSerialAndCreateTime = 4,     // ID查厅流水&开厅时间
    AnchorSerialAndIdCard = 5        // ID查主播流水&实名
}

public enum DateMode
{
    Original = 1,            // 不调整默认
    PreviousDay = 2          // 拍走时间前1天
}

public enum DeadlineMode
{
    Latest = 1,    // 最新（当前系统时间）
    Days7 = 2,     // 7日（含拍走当天起算7天，即拍走时间+6天 23:59:59）
    Days15 = 3,    // 15日（含拍走当天起算15天，即拍走时间+14天 23:59:59）
    Days30 = 4     // 30日（含拍走当天起算30天，即拍走时间+29天 23:59:59）
}