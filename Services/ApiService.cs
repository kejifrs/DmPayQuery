using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using DmPayQuery.Models;

namespace DmPayQuery.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.enlargemagic.com/api";

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    public async Task<bool> CheckTokenValidityAsync(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/admin/userCheckAdmin/getlist.action?type=1&erbanNoList=1000000");
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return false;

            var text = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.GetProperty("code").GetInt32() == 403 &&
                root.GetProperty("message").GetString()?.Contains("授权过期") == true)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool success, string message)> GetVerificationCodeAsync(string account, string password)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("account", account),
                new KeyValuePair<string, string>("password", password)
            });

            var response = await _httpClient.PostAsync($"{BaseUrl}/login/getCode", content);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"验证码响应: {text}");

            var result = JsonSerializer.Deserialize<ApiResponse<object>>(text);
            if (result?.Code == 200)
                return (true, "验证码获取成功，请查看短信");

            return (false, $"获取验证码失败: {result?.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"获取验证码异常: {ex.Message}");
        }
    }

    public async Task<(bool success, string token, string message)> LoginAsync(
        string account, string password, string code)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("account", account),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("code", code)
            });

            var response = await _httpClient.PostAsync($"{BaseUrl}/login", content);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"登录响应: {text}");

            var result = JsonSerializer.Deserialize<ApiResponse<string>>(text);
            if (result?.Code == 200 && !string.IsNullOrEmpty(result.Data))
                return (true, result.Data, "登录成功！授权已缓存");

            return (false, string.Empty, "登录失败，请检查账号、密码和验证码是否正确");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"登录异常: {ex.Message}");
        }
    }

    public async Task<(decimal amount, string bizType, string error)> GetRechargeAmountAsync(
    string userValue, string token, string startDate, string endDate, bool modeQueryUid, bool modeOnlyGift)
    {
        try
        {
            var url = $"{BaseUrl}/admin/billRecordCheck/listGroup.action";
            var queryParams = modeQueryUid
                ? $"erbanNo=&uid={userValue}&currency=1&startDate={startDate}&endDate={endDate}"
                : $"erbanNo={userValue}&uid=&currency=1&startDate={startDate}&endDate={endDate}";

            var request = new HttpRequestMessage(HttpMethod.Get, $"{url}?{queryParams}");
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            Debug.WriteLine($"==================");
            Debug.WriteLine($"查询用户: {userValue}");
            Debug.WriteLine($"原始响应: {text}");
            Debug.WriteLine($"==================");

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return (0, "API请求失败", $"HTTP错误: {(int)response.StatusCode}");

            // 手动解析 JSON（兼容 text/json 和无 code 的情况）
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // 检查是否有错误 code（有些接口有，有些没有）
            if (root.TryGetProperty("code", out var codeElement))
            {
                var code = codeElement.GetInt32();
                if (code == 4000 || code != 200)
                {
                    return (0, modeQueryUid ? "UID错误" : "ID错误", modeQueryUid ? "UID错误" : "ID错误");
                }
            }

            // 获取 rows 数组（直接顶层）
            List<BillGroupItem> rows = new();
            if (root.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowsElement.EnumerateArray())
                {
                    var item = new BillGroupItem();
                    if (row.TryGetProperty("objType", out var objTypeEl))
                        item.ObjType = objTypeEl.GetInt32();
                    if (row.TryGetProperty("totalActualAmount", out var amountEl))
                        item.TotalActualAmount = amountEl.GetDecimal();

                    rows.Add(item);
                }
            }

            Debug.WriteLine($"找到 {rows.Count} 条记录");

            decimal? rechargeAmount = null;
            decimal? giftAmount = null;

            foreach (var row in rows)
            {
                Debug.WriteLine($"处理: objType={row.ObjType}, amount={row.TotalActualAmount}");

                if (row.ObjType == 1)
                    rechargeAmount = row.TotalActualAmount;
                else if (row.ObjType == 5)
                    giftAmount = Math.Abs(row.TotalActualAmount);
            }

            Debug.WriteLine($"充值金额: {rechargeAmount}, 送礼金额: {giftAmount}");

            // 业务逻辑判断（和Python一致）
            if (modeOnlyGift)
                return (giftAmount ?? 0, "送礼", string.Empty);

            if (rechargeAmount.HasValue && giftAmount.HasValue)
            {
                return (rechargeAmount.Value >= giftAmount.Value)
                    ? (rechargeAmount.Value, "充值", string.Empty)
                    : (giftAmount.Value, "送礼", string.Empty);
            }
            else if (rechargeAmount.HasValue)
                return (rechargeAmount.Value, "充值", string.Empty);
            else if (giftAmount.HasValue)
                return (giftAmount.Value, "送礼", string.Empty);
            else
                return (0, "无交易", string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"查询异常: {ex}");
            return (0, "API请求失败", $"异常: {ex.Message}");
        }
    }

    public async Task<(string uid, string registerDate, string error)> GetUserInfoAsync(string userId, string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/admin/userCheckAdmin/getlist.action?type=1&erbanNoList={userId}");
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"用户信息响应: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // 检查 code
            if (!root.TryGetProperty("code", out var codeElement) || codeElement.GetInt32() != 200)
                return ("ID错误", "ID错误", "ID错误");

            if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                return ("ID错误", "ID错误", "ID错误");

            if (dataElement.GetArrayLength() == 0)
                return ("ID错误", "ID错误", "ID错误");

            var first = dataElement[0];
            if (!first.TryGetProperty("users", out var usersElement))
                return ("ID错误", "ID错误", "ID错误");

            // uid 可能是数字或字符串，兼容处理
            string uid;
            if (usersElement.TryGetProperty("uid", out var uidElement))
            {
                uid = uidElement.ValueKind == JsonValueKind.Number
                    ? uidElement.GetInt64().ToString()
                    : uidElement.GetString() ?? "ID错误";
            }
            else
            {
                uid = "ID错误";
            }

            // 注册时间
            long timestamp = 0;
            if (usersElement.TryGetProperty("agreementSignTime", out var timeElement))
            {
                timestamp = timeElement.ValueKind == JsonValueKind.Number
                    ? timeElement.GetInt64()
                    : 0;
            }

            var registerDate = timestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("yyyy-MM-dd")
            : "未知";

            return (uid, registerDate, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取用户信息异常: {ex}");
            return ("ID错误", "ID错误", "ID错误");
        }
    }
}