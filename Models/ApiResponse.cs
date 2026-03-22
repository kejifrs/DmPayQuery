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