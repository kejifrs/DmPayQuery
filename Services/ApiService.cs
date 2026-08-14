using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using DmPayQuery.Models;

namespace DmPayQuery.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://zapi.shanmiaobanyin.com/api";
    private const int DefaultAnchorSerialPageSize = 100;

    // 默认分页大小（如需在运行时配置，可扩展为通过构造函数或方法注入）
    // 使用常量 DefaultAnchorSerialPageSize 直接引用

    /// <summary>
    /// 用于区分秒级与毫秒级时间戳的阈值（约 2001-09-09 对应的秒数 1e12）。
    /// 大于此值视为毫秒时间戳，否则视为秒时间戳。
    /// </summary>
    private const long TimestampMillisecondThreshold = 1_000_000_000_000L;

    private int _anchorPageSize = DefaultAnchorSerialPageSize;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    // 统一使用真实接口前缀，不再尝试备用地址
    private async Task<(HttpResponseMessage? response, string text)> SendGetAsync(string url, string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);
            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response, text);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendGetAsync 异常: {ex}");
            return (null, string.Empty);
        }
    }

    public void SetAnchorSerialPageSize(int size)
    {
        if (size > 0) _anchorPageSize = size;
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
            // 优先使用 JSON body 的 POST（多数 modern API 接收 application/json）
            var payload = new { account, password };
            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var requestUrl = $"{BaseUrl}/admin/system/login/getCode?account={Uri.EscapeDataString(account)}&password={Uri.EscapeDataString(password)}";

            var response = await _httpClient.PostAsync(requestUrl, jsonContent);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"验证码响应 (尝试 JSON): {text}");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResponse<object>>(text);
                if (result?.Code == 200)
                    return (true, "验证码获取成功，请查看短信");
            }

            // 回退：尝试不带 body 的 POST（某些后端只关心 query string）
            try
            {
                var emptyContent = new StringContent(string.Empty);
                var fallbackResp = await _httpClient.PostAsync(requestUrl, emptyContent);
                var fallbackText = await fallbackResp.Content.ReadAsStringAsync();
                Debug.WriteLine($"验证码响应 (回退 POST): {fallbackText}");

                if (fallbackResp.IsSuccessStatusCode)
                {
                    var result2 = JsonSerializer.Deserialize<ApiResponse<object>>(fallbackText);
                    if (result2?.Code == 200)
                    return (true, "验证码获取成功，请查看短信");
                    return (false, $"获取验证码失败: {result2?.EffectiveMessage} / {fallbackText}");
                }

                return (false, $"获取验证码失败: HTTP {(int)fallbackResp.StatusCode} / {fallbackText}");
            }
            catch (Exception ex2)
            {
                return (false, $"获取验证码失败: {ex2.Message} / 响应: {text}");
            }
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
            // 使用与前端相同的字段名：username / password / smsCode
            var payload = new { username = account, password = password, smsCode = code };
            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{BaseUrl}/admin/system/login", jsonContent);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"登录响应: {text}");

            var result = JsonSerializer.Deserialize<ApiResponse<LoginData>>(text);
            if (response.IsSuccessStatusCode && result?.Code == 200 && result.Data != null && !string.IsNullOrEmpty(result.Data.AccessToken))
                return (true, result.Data.AccessToken, "登录成功！授权已缓存");

            return (false, string.Empty, $"登录失败: {(result != null ? result.EffectiveMessage : text)}");
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
            // 新版 API 路径与参数（示例：/admin/system/admin/billRecordCheck/listGroup?userNumber=...&currency=0&startDate=...&endDate=...&uid=...&pageNumber=1）
            var url = $"{BaseUrl}/admin/system/admin/billRecordCheck/listGroup";

            var encodedStart = Uri.EscapeDataString(startDate);
            var encodedEnd = Uri.EscapeDataString(endDate);
            string queryParams;

            if (modeQueryUid)
            {
                // 按 UID 查询：userNumber 为空，uid 填写
                queryParams = $"userNumber=&currency=0&startDate={encodedStart}&endDate={encodedEnd}&uid={Uri.EscapeDataString(userValue)}&pageNumber=1";
            }
            else
            {
                // 按账号/ID 查询：userNumber 填写，uid 为空
                queryParams = $"userNumber={Uri.EscapeDataString(userValue)}&currency=0&startDate={encodedStart}&endDate={encodedEnd}&uid=&pageNumber=1";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"{url}?{queryParams}");
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            Debug.WriteLine("==================");
            Debug.WriteLine($"查询用户: {userValue}");
            Debug.WriteLine($"请求 URL: {url}?{queryParams}");
            Debug.WriteLine($"原始响应: {text}");
            Debug.WriteLine("==================");

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return (0, "API请求失败", $"HTTP错误: {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // 某些返回带 code 字段并在非200时说明出错
            if (root.TryGetProperty("code", out var codeElement))
            {
                var code = codeElement.GetInt32();
                if (code != 200)
                    return (0, modeQueryUid ? "UID错误" : "ID错误", modeQueryUid ? "UID错误" : "ID错误");
            }

            // 新版可能将数据放在 data.rows / rows / list 中，尝试兼容多种情况
            JsonElement rowsEl = default;
            bool found = false;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                if (dataEl.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                    found = true;
                else if (dataEl.TryGetProperty("list", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                    found = true;
            }

            if (!found && root.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;
            if (!found && root.TryGetProperty("list", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;

            // 支持 data 直接为数组的情况
            if (!found && root.TryGetProperty("data", out var dataTop) && dataTop.ValueKind == JsonValueKind.Array)
            {
                rowsEl = dataTop;
                found = true;
            }

            decimal? rechargeAmount = null;
            decimal? giftAmount = null;

            // 我们对可能的多条记录进行累加（更稳健），同时兼容 objType 数字和 objTypeDesc 文本
            if (found)
            {
                decimal rechargeSum = 0m;
                decimal giftSum = 0m;
                bool hasRecharge = false;
                bool hasGift = false;

                foreach (var r in rowsEl.EnumerateArray())
                {
                    decimal val = 0m;
                    if (r.TryGetProperty("totalActualAmount", out var amountEl))
                    {
                        if (amountEl.ValueKind == JsonValueKind.Number)
                            val = amountEl.GetDecimal();
                        else
                        {
                            var s = GetJsonElementString(amountEl);
                            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out val);
                        }
                    }

                    if (r.TryGetProperty("objType", out var objTypeEl) && objTypeEl.ValueKind == JsonValueKind.Number)
                    {
                        var ot = objTypeEl.GetInt32();
                        if (ot == 1)
                        {
                            rechargeSum += val;
                            hasRecharge = true;
                        }
                        else if (ot == 5)
                        {
                            giftSum += Math.Abs(val);
                            hasGift = true;
                        }
                    }
                    else if (r.TryGetProperty("objTypeDesc", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                    {
                        var desc = GetJsonElementString(descEl);
                        if (desc.Contains("充值"))
                        {
                            rechargeSum += val;
                            hasRecharge = true;
                        }
                        else if (desc.Contains("礼") || desc.Contains("收礼") || desc.Contains("送礼"))
                        {
                            giftSum += Math.Abs(val);
                            hasGift = true;
                        }
                    }
                    else if (r.TryGetProperty("sourceType", out var sourceEl))
                    {
                        // 回退规则：部分接口用 sourceType 表示收礼等行为（样例中收礼 sourceType=6）
                        var st = GetJsonElementInt32(sourceEl);
                        if (st == 6)
                        {
                            giftSum += Math.Abs(val);
                            hasGift = true;
                        }
                    }
                }

                if (hasRecharge) rechargeAmount = rechargeSum;
                if (hasGift) giftAmount = giftSum;
            }

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
            // 新版接口使用 POST，路径 /admin/system/admin/userCheckAdmin/getlist，参数放在查询字符串
            var url = $"{BaseUrl}/admin/system/admin/userCheckAdmin/getlist?type=1&erbanNoList={Uri.EscapeDataString(userId)}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", token);
            // 发送空 JSON 对象并设置 Content-Type 为 application/json，避免 415 错误
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"用户信息响应: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // 检查 code
            if (!root.TryGetProperty("code", out var codeElement) || codeElement.GetInt32() != 200)
                return ("ID错误", "ID错误", "ID错误");

            // data 可能是数组，数组内元素可能包含 users 或 account
            if (!root.TryGetProperty("data", out var dataElement))
                return ("ID错误", "ID错误", "ID错误");

            JsonElement first;
            if (dataElement.ValueKind == JsonValueKind.Array && dataElement.GetArrayLength() > 0)
                first = dataElement[0];
            else if (dataElement.ValueKind == JsonValueKind.Object)
                first = dataElement;
            else
                return ("ID错误", "ID错误", "ID错误");

            // 优先从 users.uid 查找
            string uid = "ID错误";
            if (first.TryGetProperty("users", out var usersElement) && usersElement.ValueKind == JsonValueKind.Object)
            {
                if (usersElement.TryGetProperty("uid", out var uidElement))
                {
                    uid = uidElement.ValueKind == JsonValueKind.Number
                        ? uidElement.GetInt64().ToString()
                        : uidElement.GetString() ?? "ID错误";
                }
            }

            // 回退：有些接口把 uid 放在顶层字段 uid 或 account.uid
            if (uid == "ID错误")
            {
                if (first.TryGetProperty("uid", out var topUidEl))
                {
                    uid = topUidEl.ValueKind == JsonValueKind.Number
                        ? topUidEl.GetInt64().ToString()
                        : topUidEl.GetString() ?? "ID错误";
                }
                else if (first.TryGetProperty("account", out var accountEl) && accountEl.ValueKind == JsonValueKind.Object && accountEl.TryGetProperty("uid", out var accUid))
                {
                    uid = accUid.ValueKind == JsonValueKind.Number
                        ? accUid.GetInt64().ToString()
                        : accUid.GetString() ?? "ID错误";
                }
            }

            // 注册时间：优先 users.agreementSignTime（时间戳），其次 account.signTime（字符串）或 loginTime
            string registerDate = "未知";
            if (first.TryGetProperty("users", out usersElement) && usersElement.ValueKind == JsonValueKind.Object)
            {
                if (usersElement.TryGetProperty("agreementSignTime", out var timeElement) && timeElement.ValueKind == JsonValueKind.Number)
                {
                    var ts = timeElement.GetInt64();
                    // 兼容秒/毫秒
                    registerDate = ts > TimestampMillisecondThreshold
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("yyyy-MM-dd")
                        : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("yyyy-MM-dd");
                }
                // 回退：有些返回使用 createTime 字符串表示创建时间
                else if (usersElement.TryGetProperty("createTime", out var createEl) && createEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(createEl.GetString(), out var dtc))
                        registerDate = dtc.ToString("yyyy-MM-dd");
                }
            }

            if (registerDate == "未知")
            {
                if (first.TryGetProperty("account", out var accountEl) && accountEl.ValueKind == JsonValueKind.Object && accountEl.TryGetProperty("signTime", out var signEl) && signEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(signEl.GetString(), out var dt))
                        registerDate = dt.ToString("yyyy-MM-dd");
                }
                else if (first.TryGetProperty("loginTime", out var loginEl) && loginEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(loginEl.GetString(), out var dt2))
                        registerDate = dt2.ToString("yyyy-MM-dd");
                }
            }

            return (uid, registerDate, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取用户信息异常: {ex}");
            return ("ID错误", "ID错误", "ID错误");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 模式4：厅流水（totalGold）& 开厅时间
    // ──────────────────────────────────────────────────────────────

    public async Task<(long totalGold, string error)> GetRoomSerialAsync(
        string roomId, string token, string startTime, string endTime)
    {
        try
        {
            var encodedId  = Uri.EscapeDataString(roomId);
            var normalizedStart = NormalizeDateOnly(startTime);
            var normalizedEnd = NormalizeDateOnly(endTime);
            var encodedStart = Uri.EscapeDataString(normalizedStart);
            var encodedEnd   = Uri.EscapeDataString(normalizedEnd);
            // 与后台实测一致：erbanNos + startTime/endTime(yyyy-MM-dd)
            var url = $"{BaseUrl}/admin/system/admin/roomSerial/listByPage" +
                      $"?pageNumber=1&pageSize=10&erbanNos={encodedId}" +
                      $"&startTime={encodedStart}&endTime={encodedEnd}&isPermit=1&level=0";

            var (response, text) = await SendGetAsync(url, token);
            if (response == null)
                return (0, "请求失败");

            Debug.WriteLine($"RoomSerial {roomId} (fixed): {text}");

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return (0, $"HTTP错误: {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
            {
                var msg = root.TryGetProperty("message", out var msgEl)
                    ? GetJsonElementString(msgEl)
                    : string.Empty;
                return string.IsNullOrEmpty(msg)
                    ? (0, $"API错误: {codeEl.GetInt32()}")
                    : (0, $"API错误: {codeEl.GetInt32()} - {msg}");
            }

            if (TryExtractRoomSerial(root, roomId, out var totalGold))
                return (totalGold, string.Empty);

            return (0, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetRoomSerial异常({roomId}): {ex}");
            return (0, $"异常: {ex.Message}");
        }
    }

    private static bool TryExtractRoomSerial(JsonElement root, string roomId, out long totalGold)
    {
        totalGold = 0;
        JsonElement rowsEl = default;
        bool found = false;

        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
        {
            if (dataEl.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;
            else if (dataEl.TryGetProperty("list", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;
        }

        if (!found)
        {
            if (root.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;
            else if (root.TryGetProperty("list", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                found = true;
        }

        if (!found)
            return false;

        foreach (var item in rowsEl.EnumerateArray())
        {
            if (ItemMatchesRoomId(item, roomId) && item.TryGetProperty("totalGold", out var goldEl))
            {
                totalGold = GetJsonElementInt64(goldEl);
                return true;
            }
        }

        if (rowsEl.GetArrayLength() > 0)
        {
            var first = rowsEl[0];
            if (first.TryGetProperty("totalGold", out var goldEl2))
            {
                totalGold = GetJsonElementInt64(goldEl2);
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDateOnly(string value)
    {
        if (DateTime.TryParse(value, out var dt))
            return dt.ToString("yyyy-MM-dd");

        return value.Length >= 10 ? value[..10] : value;
    }

    private static bool ItemMatchesRoomId(JsonElement item, string roomId)
    {
        string[] keys = ["erbanNo", "userErbanNo", "guildId", "leaderId", "roomUid"];
        foreach (var key in keys)
        {
            if (item.TryGetProperty(key, out var el) && GetJsonElementString(el) == roomId)
                return true;
        }

        return false;
    }
    public async Task<(string createDate, string error)> GetGuildCreateTimeAsync(
        string roomId, string token)
    {
        try
        {
            var encodedId = Uri.EscapeDataString(roomId);
            var url = $"{BaseUrl}/admin/system/admin/guild/guild/list" +
                      $"?roomErbanNo={encodedId}&pageNumber=1&pageSize=10" +
                      "&startDate=&endDate=&creator=&name=&guildBizId=&leaderErbanNo=" +
                      "&erbanNo=&status=&isSettingMargin=&isSettingHighQuality=" +
                      "&type=&isCustomCommission=";
            // 使用 POST JSON body，包含常用查询字段（pageNum/pageSize/roomUserNumber/startDate/endDate）以匹配前端请求
            var payload = new { pageNum = 1, pageSize = 10, startDate = (string?)null, endDate = (string?)null, roomUserNumber = roomId };
            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", token);
            request.Content = jsonContent;
            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (response == null)
                return (string.Empty, "请求失败");

            Debug.WriteLine($"GuildCreate {roomId}: {text}");

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return (string.Empty, $"HTTP错误: {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
                return (string.Empty, $"API错误: {codeEl.GetInt32()}");

            // Find rows array: could be data.rows, data.list, or top-level rows/list
            JsonElement listEl = default;
            bool found = false;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                if (dataEl.TryGetProperty("rows", out listEl) && listEl.ValueKind == JsonValueKind.Array)
                    found = true;
                else if (dataEl.TryGetProperty("list", out listEl) && listEl.ValueKind == JsonValueKind.Array)
                    found = true;
            }
            if (!found && root.TryGetProperty("rows", out listEl) && listEl.ValueKind == JsonValueKind.Array)
                found = true;
            if (!found && root.TryGetProperty("list", out listEl) && listEl.ValueKind == JsonValueKind.Array)
                found = true;

            if (!found || listEl.GetArrayLength() == 0)
                return (string.Empty, string.Empty);

            var firstItem = listEl[0];
            if (!firstItem.TryGetProperty("createTime", out var ctEl))
                return (string.Empty, string.Empty);

            // 支持数值时间戳（秒/毫秒）和字符串时间（"yyyy-MM-dd HH:mm:ss" 等）
            if (ctEl.ValueKind == JsonValueKind.Number)
            {
                var ts = GetJsonElementInt64(ctEl);
                if (ts <= 0)
                    return (string.Empty, string.Empty);
                var dtoNum = ts > TimestampMillisecondThreshold
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts)
                    : DateTimeOffset.FromUnixTimeSeconds(ts);
                return (dtoNum.ToLocalTime().ToString("yyyy-MM-dd"), string.Empty);
            }

            if (ctEl.ValueKind == JsonValueKind.String)
            {
                var s = ctEl.GetString();
                if (string.IsNullOrEmpty(s))
                    return (string.Empty, string.Empty);
                if (DateTime.TryParse(s, out var dt))
                    return (dt.ToString("yyyy-MM-dd"), string.Empty);
                if (long.TryParse(s, out var sval))
                {
                    var dtoStr = sval > TimestampMillisecondThreshold
                        ? DateTimeOffset.FromUnixTimeMilliseconds(sval)
                        : DateTimeOffset.FromUnixTimeSeconds(sval);
                    return (dtoStr.ToLocalTime().ToString("yyyy-MM-dd"), string.Empty);
                }
                return (string.Empty, string.Empty);
            }

            return (string.Empty, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetGuildCreate异常({roomId}): {ex}");
            return (string.Empty, $"异常: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 模式5：ID查主播流水（totalGoldNum）& 身份证号
    // ──────────────────────────────────────────────────────────────

    public async Task<(long totalGold, string error)> GetAnchorSerialAsync(
        string anchorId, string token, string startTime, string endTime)
    {
        try
        {
            var encodedId    = Uri.EscapeDataString(anchorId);
            var encodedStart = Uri.EscapeDataString(startTime);
            var encodedEnd   = Uri.EscapeDataString(endTime);

            long totalGold = 0;
            var pageNum = 1;
            int? total = null;

            while (true)
            {
                var url = $"{BaseUrl}/admin/system/admin/giftSend/list" +
                          $"?pageNum={pageNum}&pageSize={_anchorPageSize}&roomErbanNo=&sendErbanNo=" +
                          $"&reciveErbanNo={encodedId}&startTime={encodedStart}" +
                          $"&endTime={encodedEnd}&groupType=1&guildName=";

                var (response, text) = await SendGetAsync(url, token);
                if (response == null)
                    return (0, "请求失败");

                Debug.WriteLine($"AnchorSerial {anchorId} page {pageNum}: {text}");

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    return (0, $"HTTP错误: {(int)response.StatusCode}");

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
                    return (0, $"API错误: {codeEl.GetInt32()}");

                if (!TryGetAnchorSerialRows(root, out var rowsEl, out var pageTotal))
                    return (0, string.Empty);

                total ??= pageTotal;

                if (total == 0 || rowsEl.GetArrayLength() == 0)
                    return (0, string.Empty);

                totalGold += SumAnchorSerialTotalGoldNum(rowsEl, anchorId);

                if (pageNum * _anchorPageSize >= total || rowsEl.GetArrayLength() < _anchorPageSize)
                    break;

                pageNum++;
            }

            return (totalGold, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetAnchorSerial异常({anchorId}): {ex}");
            return (0, $"异常: {ex.Message}");
        }
    }

    private static bool TryGetAnchorSerialRows(JsonElement root, out JsonElement rowsEl, out int total)
    {
        total = 0;

        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
        {
            if (dataEl.TryGetProperty("total", out var wrappedTotalEl))
                total = GetJsonElementInt32(wrappedTotalEl);

            if (dataEl.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                return true;
        }

        if (root.TryGetProperty("total", out var totalEl))
            total = GetJsonElementInt32(totalEl);

        return root.TryGetProperty("rows", out rowsEl) && rowsEl.ValueKind == JsonValueKind.Array;
    }

    private static long SumAnchorSerialTotalGoldNum(JsonElement rowsEl, string anchorId)
    {
        long matchedTotalGoldNum = 0;
        long fallbackTotalGoldNum = 0;
        var hasMatchedAnchor = false;

        foreach (var item in rowsEl.EnumerateArray())
        {
            if (!item.TryGetProperty("totalGoldNum", out var goldEl))
                continue;

            var currentGoldNum = GetJsonElementInt64(goldEl);
            fallbackTotalGoldNum += currentGoldNum;

            if (item.TryGetProperty("reciveErbanNo", out var idEl) && GetJsonElementString(idEl) == anchorId)
            {
                matchedTotalGoldNum += currentGoldNum;
                hasMatchedAnchor = true;
            }
        }

        return hasMatchedAnchor ? matchedTotalGoldNum : fallbackTotalGoldNum;
    }

    private static string GetJsonElementString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            _ => string.Empty
        };
    }

    private static long GetJsonElementInt64(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var value) => value,
                JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => (long)Math.Truncate(decimalValue),
                JsonValueKind.Number when double.TryParse(element.GetRawText(), NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue) => (long)Math.Truncate(doubleValue),
                JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringLong) => stringLong,
                JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringDecimal) => (long)Math.Truncate(stringDecimal),
                JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var currentLong) => currentLong,
                JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var currentDecimal) => (long)Math.Truncate(currentDecimal),
                _ => 0L
            };
        }
        catch
        {
            return 0L;
        }
    }

    private static int GetJsonElementInt32(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt32(out var value) => value,
                JsonValueKind.Number when element.TryGetInt64(out var longValue) => (int)longValue,
                JsonValueKind.String when int.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringInt) => stringInt,
                JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringLong) => (int)stringLong,
                JsonValueKind.String when int.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var currentInt) => currentInt,
                JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var currentLong) => (int)currentLong,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    public async Task<(string idCardNum, string error)> GetUserIdCardAsync(
        string userId, string token)
    {
        try
        {
            var encodedId = Uri.EscapeDataString(userId);
            var url = $"{BaseUrl}/admin/system/admin/userCheckAdmin/getlist?type=1&erbanNoList={encodedId}";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", token);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"IdCard {userId}: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
                return (string.Empty, "ID错误");

            if (!root.TryGetProperty("data", out var dataEl))
                return (string.Empty, "无实名信息");

            JsonElement first;
            if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
                first = dataEl[0];
            else if (dataEl.ValueKind == JsonValueKind.Object)
                first = dataEl;
            else
                return (string.Empty, "无实名信息");

            string idCard = string.Empty;
            if (first.TryGetProperty("idCardNum", out var cardEl))
                idCard = cardEl.GetString() ?? string.Empty;
            else if (first.TryGetProperty("users", out var usersEl) && usersEl.TryGetProperty("idCardNum", out var cardEl2))
                idCard = cardEl2.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(idCard))
                return (string.Empty, "无实名信息");

            return (idCard, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetUserIdCard异常({userId}): {ex}");
            return (string.Empty, $"异常: {ex.Message}");
        }
    }

    public async Task<(string idCard, byte[]? avatar, string error)> GetUserIdCardAndAvatarAsync(
        string userId, string token)
    {
        try
        {
            var encodedId = Uri.EscapeDataString(userId);
            var url = $"{BaseUrl}/admin/system/admin/userCheckAdmin/getlist?type=1&erbanNoList={encodedId}";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", token);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"IdCardAndAvatar {userId}: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
                return (string.Empty, null, "ID错误");

            if (!root.TryGetProperty("data", out var dataEl))
                return (string.Empty, null, "无实名信息");

            JsonElement first;
            if (dataEl.ValueKind == JsonValueKind.Array && dataEl.GetArrayLength() > 0)
                first = dataEl[0];
            else if (dataEl.ValueKind == JsonValueKind.Object)
                first = dataEl;
            else
                return (string.Empty, null, "无实名信息");

            string idCard = string.Empty;
            string avatarUrl = string.Empty;

            if (first.TryGetProperty("users", out var usersEl))
            {
                if (usersEl.TryGetProperty("idCardNum", out var cardEl))
                    idCard = cardEl.GetString() ?? string.Empty;
                if (usersEl.TryGetProperty("avatar", out var avatarEl))
                    avatarUrl = avatarEl.GetString() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(idCard) && first.TryGetProperty("idCardNum", out var cardElDirect))
                idCard = cardElDirect.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(idCard))
                return (string.Empty, null, "无实名信息");

            byte[]? avatar = null;
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                try
                {
                    avatar = await _httpClient.GetByteArrayAsync(avatarUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"头像下载失败({userId}): {ex.Message}");
                }
            }

            return (idCard, avatar, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetUserIdCardAndAvatar异常({userId}): {ex}");
            return (string.Empty, null, $"异常: {ex.Message}");
        }
    }
}