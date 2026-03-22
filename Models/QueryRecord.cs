using CommunityToolkit.Mvvm.ComponentModel;

namespace DmPayQuery.Models;

public partial class QueryRecord : ObservableObject
{
    [ObservableProperty]
    private string _消费ID = string.Empty;

    [ObservableProperty]
    private string _消费UID = string.Empty;

    [ObservableProperty]
    private string _拍走日期 = string.Empty;

    [ObservableProperty]
    private string _金额 = string.Empty;

    [ObservableProperty]
    private string _业务类型 = string.Empty;

    [ObservableProperty]
    private string _实际查询日期 = string.Empty;

    [ObservableProperty]
    private string _注册日期 = string.Empty;
}