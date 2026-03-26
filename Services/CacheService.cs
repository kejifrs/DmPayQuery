using System.IO;
using System.Text.Json;
using DmPayQuery.Models;

namespace DmPayQuery.Services;

public class CacheService : ICacheService
{
    private readonly string _cacheFilePath;
    private const int CacheValidSeconds = 7200;

    public CacheService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "ConsumptionQueryTool");
        Directory.CreateDirectory(appFolder);
        _cacheFilePath = Path.Combine(appFolder, "login_cache.json");
    }

    public string GetCacheFilePath() => _cacheFilePath;

    public async Task<LoginCache?> GetCacheAsync()
    {
        if (!File.Exists(_cacheFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath);
            var cache = JsonSerializer.Deserialize<LoginCache>(json);

            if (cache == null) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - cache.Timestamp > CacheValidSeconds)
                return null;

            return cache;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveCacheAsync(LoginCache cache)
    {
        cache.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_cacheFilePath, json);
    }

    public async Task ClearCacheAsync()
    {
        if (File.Exists(_cacheFilePath))
        {
            await Task.Run(() => File.Delete(_cacheFilePath));
        }
    }
}