namespace DmPayQuery.Services;

public interface IApiService
{
    Task<bool> CheckTokenValidityAsync(string token);
    Task<(bool success, string message)> GetVerificationCodeAsync(string account, string password);
    Task<(bool success, string token, string message)> LoginAsync(string account, string password, string code);
    Task<(decimal amount, string bizType, string error)> GetRechargeAmountAsync(
        string userValue, string token, string startDate, string endDate, bool modeQueryUid, bool modeOnlyGift);
    Task<(string uid, string registerDate, string error)> GetUserInfoAsync(string userId, string token);
}