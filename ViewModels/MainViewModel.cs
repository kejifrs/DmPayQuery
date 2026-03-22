using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using DmPayQuery.Models;
using DmPayQuery.Services;
using DmPayQuery.Views;

namespace DmPayQuery.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly ICacheService _cacheService;
    private readonly IExcelService _excelService;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private QueryMode _queryMode = QueryMode.IdRechargeOrGift;

    [ObservableProperty]
    private DateMode _dateMode = DateMode.Original;

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logs = new();

    [ObservableProperty]
    private bool _isQuerying;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressVisible;

    private string? _currentToken;
    private DataTable? _currentDataTable;

    public MainViewModel(IApiService apiService, ICacheService cacheService, IExcelService excelService)
    {
        _apiService = apiService;
        _cacheService = cacheService;
        _excelService = excelService;
    }

    [RelayCommand]
    private void SelectFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            Title = "选择待查询的Excel文件"
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
            AddLog($"📁 已选择文件: {Path.GetFileName(FilePath)}", "Blue");
        }
    }

    [RelayCommand]
    private async Task StartQuery()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            MessageBox.Show("请先选择待查询的Excel文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(FilePath) ?? AppDomain.CurrentDomain.BaseDirectory,
            "查询结果.xlsx");

        if (!await _excelService.CheckFileWritableAsync(outputPath))
        {
            AddLog("❌ 输出文件被占用，请关闭“查询结果.xlsx”后重试", "Red");
            return;
        }

        IsQuerying = true;
        ProgressVisible = true;
        ProgressValue = 0;

        try
        {
            // 1. 登录验证
            AddLog("🔐 登录验证中...", "Cyan");
            _currentToken = await LoginWithCacheAsync();

            if (string.IsNullOrEmpty(_currentToken))
            {
                AddLog("💡 登录取消，查询终止", "Orange");
                return;
            }

            // 2. 读取 Excel
            AddLog("🔧 开始前置检查...", "Cyan");
            _currentDataTable = await _excelService.ReadExcelAsync(FilePath);

            if (_currentDataTable == null)
            {
                AddLog("❌ 读取Excel失败", "Red");
                return;
            }

            // 清理空白列
            var columnsToRemove = new List<DataColumn>();
            foreach (DataColumn col in _currentDataTable.Columns)
            {
                if (col.ColumnName.StartsWith("Column") || string.IsNullOrWhiteSpace(col.ColumnName))
                {
                    columnsToRemove.Add(col);
                }
            }

            foreach (var col in columnsToRemove)
            {
                _currentDataTable.Columns.Remove(col);
                AddLog($"🗑️ 移除空白列: {col.ColumnName}", "Gray");
            }

            AddLog("✅ 前置检查通过，读取Excel...", "Green");
            AddLog($"📊 成功读取 {_currentDataTable.Rows.Count} 条数据", "Blue");

            // 3. 确保列存在
            EnsureColumnsExist();

            // 4. 批量查询
            AddLog("🚀 开始批量查询（并发控制）...", "Green");
            var startTime = DateTime.Now;
            var stats = new QueryStats();

            var semaphore = new SemaphoreSlim(QueryMode == QueryMode.UidRechargeOrGift ? 20 : 6);
            var tasks = new List<Task>();

            for (int i = 0; i < _currentDataTable.Rows.Count; i++)
            {
                var rowIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ProcessRowAsync(rowIndex, stats);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // 5. 统计（先显示统计）
            var duration = DateTime.Now - startTime;
            AddLog("\n================= 任务统计 =================", "Purple");
            AddLog($"🧩 版本：v3.2.0", "Cyan");
            AddLog($"🔍 模式：{GetModeName()}", "Cyan");
            AddLog($"📅 拍走日期：{(DateMode == DateMode.PreviousDay ? "拍走日期前1天" : "原始日期")}", "Cyan");
            AddLog($"📊 总条数：{stats.TotalCount}", "Cyan");
            AddLog($"✅ 成功：{stats.SuccessCount}", "Green");
            AddLog($"⚠️  失败：{stats.FailCount}", "Orange");
            AddLog($"🕒 耗时：{duration.TotalSeconds:F2} 秒", "Cyan");
            AddLog("========================================", "Purple");
            AddLog("🎉 查询任务全部完成！", "Green");

            // 6. 保存结果（统计之后）
            try
            {
                await _excelService.SaveExcelAsync(_currentDataTable, outputPath);
                AddLog($"📁 结果已保存：{outputPath}", "Blue");

                // 7. 自动打开（最后）
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outputPath)
                    {
                        UseShellExecute = true
                    });
                    AddLog("✨ 已自动打开结果文件", "Cyan");
                }
                catch
                {
                    AddLog("⚠️ 自动打开失败，请手动打开", "Orange");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ 保存文件失败：{ex.Message}", "Red");
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ 查询异常终止：{ex.Message}", "Red");
        }
        finally
        {
            IsQuerying = false;
            ProgressVisible = false;
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        AddLog("🗑️  日志已清空", "Cyan");
    }

    private async Task<string?> LoginWithCacheAsync()
    {
        var cache = await _cacheService.GetCacheAsync();

        if (cache != null)
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.Timestamp;
            var remaining = 3600 - elapsed;
            var loginTime = DateTimeOffset.FromUnixTimeSeconds(cache.Timestamp).ToLocalTime().ToString("HH:mm:ss");

            AddLog($"🟢 检测到有效缓存【账号：{cache.Account} | 登录时间：{loginTime} | 剩余：{remaining / 60}分{remaining % 60}秒】", "Green");
            AddLog("🔍 验证缓存有效性...", "Cyan");

            if (await _apiService.CheckTokenValidityAsync(cache.Token))
            {
                return cache.Token;
            }

            AddLog("⚠️ Token已过期，重新登录...", "Orange");
            await _cacheService.ClearCacheAsync();
        }
        else
        {
            AddLog("⚠️ 无有效缓存，请完成登录", "Orange");
        }

        // 显示登录对话框
        var loginDialog = new LoginDialog(_apiService);
        // center over main window
        if (Application.Current?.MainWindow != null)
            loginDialog.Owner = Application.Current.MainWindow;

        if (loginDialog.ShowDialog() == true && loginDialog.ViewModel.IsLoggedIn)
        {
            var token = loginDialog.ViewModel.Token;
            var account = loginDialog.ViewModel.Account;

            await _cacheService.SaveCacheAsync(new LoginCache
            {
                Token = token,
                Account = account
            });

            AddLog($"😎 登录成功！授权已缓存", "Green");
            AddLog($"🕒 当前登录时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}", "Cyan");

            return token;
        }

        return null;
    }

    private async Task ProcessRowAsync(int rowIndex, QueryStats stats)
    {
        var row = _currentDataTable!.Rows[rowIndex];
        var userValue = QueryMode == QueryMode.UidRechargeOrGift
            ? row["消费UID"]?.ToString()
            : row["消费ID"]?.ToString();

        if (string.IsNullOrEmpty(userValue))
        {
            stats.FailCount++;
            stats.TotalCount++;
            AddLog($"❌ 第{rowIndex + 1}行：查询值为空，请检查Excel列名！", "Red");
            return;
        }

        AddLog($"🔍 第{rowIndex + 1}行：查询 {userValue}", "Cyan");

        // 处理日期
        var dateStr = row["拍走日期"]?.ToString() ?? "2022-11-01";
        DateTime date;
        if (!DateTime.TryParse(dateStr, out date))
            date = new DateTime(2022, 11, 1);

        var originalDate = date.ToString("yyyy-MM-dd");
        var actualDate = originalDate;

        if (DateMode == DateMode.PreviousDay)
        {
            date = date.AddDays(-1);
            actualDate = date.ToString("yyyy-MM-dd");
        }

        var startDate = $"{actualDate} 00:00:00";
        var endDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 查询金额
        var modeOnlyGift = QueryMode == QueryMode.IdGiftOnly;
        var (amount, bizType, error) = await _apiService.GetRechargeAmountAsync(
            userValue, _currentToken!, startDate, endDate,
            QueryMode == QueryMode.UidRechargeOrGift,
            modeOnlyGift);

        row["金额"] = amount.ToString();
        row["业务类型"] = bizType;
        row["实际查询日期"] = actualDate;

        // 如果不是UID模式，查询用户信息
        if (QueryMode != QueryMode.UidRechargeOrGift)
        {
            var (uid, registerDate, userError) = await _apiService.GetUserInfoAsync(userValue, _currentToken!);
            row["消费UID"] = uid;
            row["注册日期"] = registerDate;
        }

        // 更新统计
        if (error == "ID错误" || error == "UID错误" || error == "API请求失败")
        {
            stats.FailCount++;
            AddLog($"❌ 第{rowIndex + 1}行：{userValue} 查询失败 - {error}", "Red");
        }
        else
        {
            stats.SuccessCount++;
            AddLog($"✅ 第{rowIndex + 1}行：{userValue} 查询成功 - 金额：{amount} | 类型：{bizType}", "Green");
        }

        stats.TotalCount++;

        // 更新进度
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressValue = (double)stats.TotalCount / _currentDataTable!.Rows.Count * 100;
        });
    }

    private void EnsureColumnsExist()
    {
        var requiredCols = new[] { "金额", "业务类型", "实际查询日期" };
        if (QueryMode != QueryMode.UidRechargeOrGift)
        {
            requiredCols = new[] { "消费UID", "注册日期" }.Concat(requiredCols).ToArray();
        }

        foreach (var col in requiredCols)
        {
            if (!_currentDataTable!.Columns.Contains(col))
                _currentDataTable.Columns.Add(col);
        }
    }

    private string GetModeName() => QueryMode switch
    {
        QueryMode.IdRechargeOrGift => "ID查充值/送礼（取最大值）",
        QueryMode.IdGiftOnly => "ID只查送礼",
        QueryMode.UidRechargeOrGift => "UID查充值/送礼",
        _ => "未知"
    };

    private void AddLog(string message, string color)
    {
        var timestamp = DateTime.Now.ToString("[HH:mm:ss]");
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry { Message = $"{timestamp} {message}", Color = color });
        });
    }
}

public class QueryStats
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
}