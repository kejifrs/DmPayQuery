namespace DmPayQuery.Models;

public class LoginCache
{
    public string Token { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Account { get; set; } = string.Empty;
}