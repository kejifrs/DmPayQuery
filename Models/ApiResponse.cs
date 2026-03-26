using System.Text.Json.Serialization;

namespace DmPayQuery.Models;

public class ApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class BillGroupItem
{
    [JsonPropertyName("objType")]
    public int ObjType { get; set; }

    [JsonPropertyName("totalActualAmount")]
    public decimal TotalActualAmount { get; set; }
}

public class BillGroupResponse
{
    [JsonPropertyName("rows")]
    public List<BillGroupItem> Rows { get; set; } = new();
}

public class UserInfo
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("agreementSignTime")]
    public long AgreementSignTime { get; set; }
}

public class UserCheckData
{
    [JsonPropertyName("users")]
    public UserInfo Users { get; set; } = new();
}

// ──────────── 模式4：厅流水 & 开厅时间 ────────────

public class RoomSerialItem
{
    [JsonPropertyName("erbanNo")]
    public string ErbanNo { get; set; } = string.Empty;

    /// <summary>API 返回的原始厅流水值，展示时除以100取整。</summary>
    [JsonPropertyName("totalGold")]
    public long TotalGold { get; set; }
}

public class GuildItem
{
    [JsonPropertyName("roomErbanNo")]
    public string RoomErbanNo { get; set; } = string.Empty;

    /// <summary>Unix 时间戳（毫秒或秒），需转换为 yyyy-MM-dd。</summary>
    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }
}

// ──────────── 模式5：主播流水 & 身份证号 ────────────

public class AnchorSerialItem
{
    [JsonPropertyName("reciveErbanNo")]
    public string ReciveErbanNo { get; set; } = string.Empty;

    /// <summary>API 返回的原始主播流水值，展示时除以100取整。</summary>
    [JsonPropertyName("totalGoldNum")]
    public long TotalGoldNum { get; set; }
}

public class UserIdCardItem
{
    [JsonPropertyName("erbanNo")]
    public string ErbanNo { get; set; } = string.Empty;

    [JsonPropertyName("idCardNum")]
    public string IdCardNum { get; set; } = string.Empty;
}