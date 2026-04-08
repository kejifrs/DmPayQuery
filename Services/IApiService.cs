using DmPayQuery.Models;

namespace DmPayQuery.Services;

public interface IApiService
{
    /// <summary>验证 Token 是否有效</summary>
    Task<bool> CheckTokenValidityAsync(string token);

    /// <summary>获取登录验证码</summary>
    Task<(bool success, string message)> GetVerificationCodeAsync(string account, string password);

    /// <summary>账号密码+验证码登录，成功返回 Token</summary>
    Task<(bool success, string token, string message)> LoginAsync(string account, string password, string code);

    /// <summary>消费模式（1/2/3）：查询充值/送礼金额</summary>
    Task<(decimal amount, string bizType, string error)> GetRechargeAmountAsync(
        string userValue, string token, string startDate, string endDate, bool modeQueryUid, bool modeOnlyGift);

    /// <summary>消费模式（1/2）：查询用户 UID 与注册日期</summary>
    Task<(string uid, string registerDate, string error)> GetUserInfoAsync(string userId, string token);

    /// <summary>模式4：查询厅流水（totalGold 原始值，展示时除以100取整）</summary>
    Task<(long totalGold, string error)> GetRoomSerialAsync(
        string roomId, string token, string startTime, string endTime);

    /// <summary>模式4：查询开厅时间（时间戳转换为 yyyy-MM-dd）</summary>
    Task<(string createDate, string error)> GetGuildCreateTimeAsync(
        string roomId, string token);

    /// <summary>模式5：查询主播流水（totalGoldNum 原始值，展示时除以100取整）</summary>
    Task<(long totalGoldNum, string error)> GetAnchorSerialAsync(
        string anchorId, string token, string startTime, string endTime);

    /// <summary>模式5：查询用户实名身份证号（原始值直接返回）</summary>
    Task<(string idCardNum, string error)> GetUserIdCardAsync(
        string userId, string token);

    /// <summary>模式5：同时查询身份证号和主播头像（合并接口减少请求）</summary>
    Task<(string idCardNum, byte[]? avatarBytes, string error)> GetUserIdCardAndAvatarAsync(
        string userId, string token);
}
