using DmPayQuery.Models;

namespace DmPayQuery.Services;

public interface ICacheService
{
    Task<LoginCache?> GetCacheAsync();
    Task SaveCacheAsync(LoginCache cache);
    Task ClearCacheAsync();
    string GetCacheFilePath();
}