using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using DmPayQuery.Models;

namespace DmPayQuery.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.enlargemagic.com/api";
    private const int AnchorSerialPageSize = 100;

    /// <summary>
    /// 用于区分秒级与毫秒级时间戳的阈值（约 2001-09-09 对应的秒数 1e12）。
    /// 大于此值视为毫秒时间戳，否则视为秒时间戳。
    /// </summary>
    private const long TimestampMillisecondThreshold = 1_000_000_000_000L;

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
            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("account", account),
                new KeyValuePair<string, string>("password", password)
            ]);

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
            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("account", account),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("code", code)
            ]);

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
            List<BillGroupItem> rows = [];
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
            var url = $"{BaseUrl}/admin/roomSerial/listByPage" +
                      $"?pageNumber=1&pageSize=10&erbanNos={encodedId}" +
                      $"&startTime={encodedStart}&endTime={encodedEnd}&isPermit=1&level=0";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
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

        if (root.TryGetProperty("data", out var dataEl) &&
            dataEl.ValueKind == JsonValueKind.Object &&
            dataEl.TryGetProperty("rows", out JsonElement rowsEl) &&
            rowsEl.ValueKind == JsonValueKind.Array)
        {
            // wrapped
        }
        else if (!root.TryGetProperty("rows", out rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

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
            var url = $"{BaseUrl}/admin/guild/guild/list" +
                      $"?roomErbanNo={encodedId}&pageNumber=1&pageSize=10" +
                      "&startDate=&endDate=&creator=&name=&guildBizId=&leaderErbanNo=" +
                      "&erbanNo=&status=&isSettingMargin=&isSettingHighQuality=" +
                      "&type=&isCustomCommission=";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
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

            long ts = ctEl.ValueKind == JsonValueKind.Number ? ctEl.GetInt64() : 0;
            if (ts <= 0)
                return (string.Empty, string.Empty);

            // 自动识别秒级/毫秒级时间戳
            var dto = ts > TimestampMillisecondThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(ts)
                : DateTimeOffset.FromUnixTimeSeconds(ts);

            return (dto.ToLocalTime().ToString("yyyy-MM-dd"), string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetGuildCreate异常({roomId}): {ex}");
            return (string.Empty, $"异常: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 模式5：主播流水（totalGoldNum）& 身份证号
    // ──────────────────────────────────────────────────────────────

    public async Task<(long totalGoldNum, string error)> GetAnchorSerialAsync(
        string anchorId, string token, string startTime, string endTime)
    {
        try
        {
            var encodedId    = Uri.EscapeDataString(anchorId);
            var encodedStart = Uri.EscapeDataString(startTime);
            var encodedEnd   = Uri.EscapeDataString(endTime);

            long totalGoldNum = 0;
            var pageNum = 1;
            int? total = null;

            while (true)
            {
                var url = $"{BaseUrl}/admin/giftSend/list" +
                          $"?pageNum={pageNum}&pageSize={AnchorSerialPageSize}&roomErbanNo=&sendErbanNo=" +
                          $"&reciveErbanNo={encodedId}&startTime={encodedStart}" +
                          $"&endTime={encodedEnd}&groupType=1&guildName=";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", token);

                var response = await _httpClient.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
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

                totalGoldNum += SumAnchorSerialTotalGoldNum(rowsEl, anchorId);

                if (pageNum * AnchorSerialPageSize >= total || rowsEl.GetArrayLength() < AnchorSerialPageSize)
                    break;

                pageNum++;
            }

            return (totalGoldNum, string.Empty);
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
            var url = $"{BaseUrl}/admin/userCheckAdmin/getlist.action?type=1&erbanNoList={encodedId}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"IdCard {userId}: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
                return (string.Empty, "ID错误");

            if (!root.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Array ||
                dataEl.GetArrayLength() == 0)
                return (string.Empty, "无实名信息");

            var first = dataEl[0];
            string idCard = string.Empty;

            // idCardNum may be directly on the item or inside a nested object
            if (first.TryGetProperty("idCardNum", out var cardEl))
                idCard = cardEl.GetString() ?? string.Empty;
            else if (first.TryGetProperty("users", out var usersEl) &&
                     usersEl.TryGetProperty("idCardNum", out var cardEl2))
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

    public async Task<(string idCardNum, byte[]? avatarBytes, string error)> GetUserIdCardAndAvatarAsync(
        string userId, string token)
    {
        try
        {
            var encodedId = Uri.EscapeDataString(userId);
            var url = $"{BaseUrl}/admin/userCheckAdmin/getlist.action?type=1&erbanNoList={encodedId}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);

            var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"IdCardAndAvatar {userId}: {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
                return (string.Empty, null, "ID错误");

            if (!root.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Array ||
                dataEl.GetArrayLength() == 0)
                return (string.Empty, null, "无实名信息");

            var first = dataEl[0];
            string idCard = string.Empty;
            string avatarUrl = string.Empty;

            if (first.TryGetProperty("users", out var usersEl))
            {
                if (usersEl.TryGetProperty("idCardNum", out var cardEl))
                    idCard = cardEl.GetString() ?? string.Empty;
                if (usersEl.TryGetProperty("avatar", out var avatarEl))
                    avatarUrl = avatarEl.GetString() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(idCard) &&
                first.TryGetProperty("idCardNum", out var cardElDirect))
                idCard = cardElDirect.GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(idCard))
                return (string.Empty, null, "无实名信息");

            byte[]? avatarBytes = null;
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                try
                {
                    avatarBytes = await _httpClient.GetByteArrayAsync(avatarUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"头像下载失败({userId}): {ex.Message}");
                }
            }

            return (idCard, avatarBytes, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetUserIdCardAndAvatar异常({userId}): {ex}");
            return (string.Empty, null, $"异常: {ex.Message}");
        }
    }
}