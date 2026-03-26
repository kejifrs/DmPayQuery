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

public partial class MainViewModel(IApiService apiService, ICacheService cacheService, IExcelService excelService) : ObservableObject
{
    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial QueryMode QueryMode { get; set; } = QueryMode.IdRechargeOrGift;

    [ObservableProperty]
    public partial DateMode DateMode { get; set; } = DateMode.Original;

    [ObservableProperty]
    public partial DeadlineMode DeadlineMode { get; set; } = DeadlineMode.Latest;

    /// <summary>当前激活的Tab索引：0=消费查询，1=流水/开厅日期/实名查询</summary>
    [ObservableProperty]
    public partial int ActiveTabIndex { get; set; } = 0;

    /// <summary>模式4/5的自定义开始时间（格式：yyyy-MM-dd HH:mm:ss）</summary>
    [ObservableProperty]
    public partial string CustomStartTime { get; set; } = DateTime.Today.ToString("yyyy-MM-dd 00:00:00");

    /// <summary>模式4/5的自定义截止时间（格式：yyyy-MM-dd HH:mm:ss）</summary>
    [ObservableProperty]
    public partial string CustomEndTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>是否为高级模式（模式4/5）</summary>
    public bool IsAdvancedMode => QueryMode is QueryMode.RoomSerialAndCreateTime or QueryMode.AnchorSerialAndIdCard;

    /// <summary>是否为消费模式（模式1/2/3）</summary>
    public bool IsConsumeMode => !IsAdvancedMode;

    [ObservableProperty]
    public partial ObservableCollection<LogEntry> Logs { get; set; } = new();

    [ObservableProperty]
    public partial bool IsQuerying { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool ProgressVisible { get; set; }

    private string? _currentToken;
    private DataTable? _currentDataTable;

    // 用于保护并发写入 DataTable 行的锁对象
    private readonly object _rowWriteLock = new();

    partial void OnQueryModeChanged(QueryMode value)
    {
        OnPropertyChanged(nameof(IsAdvancedMode));
        OnPropertyChanged(nameof(IsConsumeMode));
    }

    partial void OnActiveTabIndexChanged(int value)
    {
        // 切换 Tab 时自动同步查询模式
        if (value == 0 && IsAdvancedMode)
            QueryMode = QueryMode.IdRechargeOrGift;
        else if (value == 1 && IsConsumeMode)
            QueryMode = QueryMode.RoomSerialAndCreateTime;
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

        // 模式4/5 需校验自定义时间格式
        if (IsAdvancedMode)
        {
            if (!DateTime.TryParse(CustomStartTime, out _))
            {
                MessageBox.Show("开始时间格式无效，请使用 yyyy-MM-dd HH:mm:ss 格式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!DateTime.TryParse(CustomEndTime, out _))
            {
                MessageBox.Show("截止时间格式无效，请使用 yyyy-MM-dd HH:mm:ss 格式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(FilePath) ?? AppDomain.CurrentDomain.BaseDirectory,
            "查询结果.xlsx");

        if (!await excelService.CheckFileWritableAsync(outputPath))
        {
            AddLog("❌ 输出文件被占用，请关闭「查询结果.xlsx」后重试", "Red");
            return;
        }

        IsQuerying = true;
        ProgressVisible = true;
        ProgressValue = 0;

        try
        {
            // 第1步：登录验证
            AddLog("🔐 登录验证中...", "Cyan");
            _currentToken = await LoginWithCacheAsync();

            if (string.IsNullOrEmpty(_currentToken))
            {
                AddLog("💡 登录取消，查询终止", "Orange");
                return;
            }

            // 第2步：读取 Excel
            AddLog("🔧 开始前置检查...", "Cyan");
            _currentDataTable = await excelService.ReadExcelAsync(FilePath);

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
                    columnsToRemove.Add(col);
            }
            foreach (var col in columnsToRemove)
            {
                _currentDataTable.Columns.Remove(col);
                AddLog($"🗑️ 移除空白列: {col.ColumnName}", "Gray");
            }

            // 向下兼容：将旧列名"拍走日期"重命名为"拍走时间"
            if (_currentDataTable.Columns.Contains("拍走日期") && !_currentDataTable.Columns.Contains("拍走时间"))
                _currentDataTable.Columns["拍走日期"]!.ColumnName = "拍走时间";

            AddLog("✅ 前置检查通过，读取Excel...", "Green");
            AddLog($"📊 成功读取 {_currentDataTable.Rows.Count} 条数据", "Blue");

            // 第3步：确保结果列存在
            EnsureColumnsExist();

            // 第4步：并发批量查询
            AddLog("🚀 开始批量查询（并发控制）...", "Green");
            var startTime = DateTime.Now;
            var stats = new QueryStats();

            // 根据查询模式设置并发数
            int concurrency = QueryMode switch
            {
                QueryMode.UidRechargeOrGift => 20,
                QueryMode.RoomSerialAndCreateTime or QueryMode.AnchorSerialAndIdCard => 5,
                _ => 6
            };

            var semaphore = new SemaphoreSlim(concurrency);
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

            // 第5步：输出任务统计
            var duration = DateTime.Now - startTime;
            AddLog("\n================= 任务统计 =================", "Purple");
            AddLog($"🧩 版本：v3.2.1", "Cyan");
            AddLog($"🔍 模式：{GetModeName()}", "Cyan");
            if (IsConsumeMode)
                AddLog($"📅 拍走时间：{(DateMode == DateMode.PreviousDay ? "拍走时间前1天" : "原始日期")}", "Cyan");
            AddLog($"📊 总条数：{stats.TotalCount}", "Cyan");
            AddLog($"✅ 成功：{stats.SuccessCount}", "Green");
            AddLog($"⚠️  失败：{stats.FailCount}", "Orange");
            AddLog($"🕒 耗时：{duration.TotalSeconds:F2} 秒", "Cyan");
            AddLog("========================================", "Purple");
            AddLog("🎉 查询任务全部完成！", "Green");

            // 第6步：保存结果到 Excel
            try
            {
                await excelService.SaveExcelAsync(_currentDataTable, outputPath);
                AddLog($"📁 结果已保存：{outputPath}", "Blue");

                // 第7步：自动打开结果文件
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
        var cache = await cacheService.GetCacheAsync();

        if (cache != null)
        {
            var elapsed   = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cache.Timestamp;
            var remaining = 3600 - elapsed;
            var loginTime = DateTimeOffset.FromUnixTimeSeconds(cache.Timestamp).ToLocalTime().ToString("HH:mm:ss");

            AddLog($"🟢 检测到有效缓存【账号：{cache.Account} | 登录时间：{loginTime} | 剩余：{remaining / 60}分{remaining % 60}秒】", "Green");
            AddLog("🔍 验证缓存有效性...", "Cyan");

            if (await apiService.CheckTokenValidityAsync(cache.Token))
                return cache.Token;

            AddLog("⚠️ Token已过期，重新登录...", "Orange");
            await cacheService.ClearCacheAsync();
        }
        else
        {
            AddLog("⚠️ 无有效缓存，请完成登录", "Orange");
        }

        var loginDialog = new LoginDialog(apiService);
        if (Application.Current?.MainWindow != null)
            loginDialog.Owner = Application.Current.MainWindow;

        if (loginDialog.ShowDialog() == true && loginDialog.ViewModel.IsLoggedIn)
        {
            var token   = loginDialog.ViewModel.Token;
            var account = loginDialog.ViewModel.Account;

            await cacheService.SaveCacheAsync(new LoginCache
            {
                Token   = token,
                Account = account
            });

            AddLog($"😎 登录成功！授权已缓存", "Green");
            AddLog($"🕒 当前登录时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}", "Cyan");
            return token;
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────
    // 行处理入口：安全获取行后分派到对应模式处理器
    // ──────────────────────────────────────────────────────────────

    private async Task ProcessRowAsync(int rowIndex, QueryStats stats)
    {
        // 加锁安全获取行引用，防止索引越界
        DataRow? row = null;
        lock (_rowWriteLock)
        {
            if (rowIndex < _currentDataTable!.Rows.Count)
                row = _currentDataTable.Rows[rowIndex];
        }

        if (row == null)
        {
            AddLog($"⚠️ 第{rowIndex + 1}行：行索引越界，已跳过", "Orange");
            return;
        }

        try
        {
            if (IsAdvancedMode)
                await ProcessAdvancedRowAsync(row, rowIndex, stats);
            else
                await ProcessConsumeRowAsync(row, rowIndex, stats);
        }
        catch (Exception ex)
        {
            // 单行失败不影响整批任务继续执行
            lock (_rowWriteLock)
            {
                stats.FailCount++;
                stats.TotalCount++;
            }
            AddLog($"❌ 第{rowIndex + 1}行：处理异常 - {ex.Message}", "Red");
        }
        finally
        {
            // 更新进度条
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ProgressValue = (double)stats.TotalCount / _currentDataTable!.Rows.Count * 100;
            });
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 消费模式（模式1/2/3）行处理
    // ──────────────────────────────────────────────────────────────

    private async Task ProcessConsumeRowAsync(DataRow row, int rowIndex, QueryStats stats)
    {
        // 根据模式读取查询主键（消费ID 或 消费UID）
        var userValue = QueryMode == QueryMode.UidRechargeOrGift
            ? SafeGetColumn(row, "消费UID")
            : SafeGetColumn(row, "消费ID");

        if (string.IsNullOrEmpty(userValue))
        {
            lock (_rowWriteLock)
            {
                stats.FailCount++;
                stats.TotalCount++;
            }
            AddLog($"❌ 第{rowIndex + 1}行：查询值为空，请检查Excel列名！", "Red");
            return;
        }

        AddLog($"🔍 第{rowIndex + 1}行：查询 {userValue}", "Cyan");

        // 读取拍走时间（同时兼容旧列名"拍走日期"）
        var dateStr = SafeGetColumn(row, "拍走时间") ?? SafeGetColumn(row, "拍走日期") ?? "2022-11-01";
        if (!DateTime.TryParse(dateStr, out DateTime date))
            date = new DateTime(2022, 11, 1);

        // 根据日期模式决定开始时间
        if (DateMode == DateMode.PreviousDay)
            date = date.AddDays(-1);

        var startDate = $"{date:yyyy-MM-dd} 00:00:00";
        var endDate   = CalculateEndDate(date);

        var modeOnlyGift = QueryMode == QueryMode.IdGiftOnly;
        var (amount, bizType, error) = await apiService.GetRechargeAmountAsync(
            userValue, _currentToken!, startDate, endDate,
            QueryMode == QueryMode.UidRechargeOrGift, modeOnlyGift);

        // 加锁写入行数据，防止并发竞态
        lock (_rowWriteLock)
        {
            SafeSetColumn(row, "金额",         amount.ToString());
            SafeSetColumn(row, "业务类型",     bizType);
            SafeSetColumn(row, "查询开始时间", startDate);
            SafeSetColumn(row, "查询截止时间", endDate);
        }

        // 非UID模式需额外查询用户信息（UID 和注册日期）
        if (QueryMode != QueryMode.UidRechargeOrGift)
        {
            var (uid, registerDate, _) = await apiService.GetUserInfoAsync(userValue, _currentToken!);
            lock (_rowWriteLock)
            {
                SafeSetColumn(row, "消费UID", uid);
                SafeSetColumn(row, "注册日期", registerDate);
            }
        }

        bool failed = error == "ID错误" || error == "UID错误" || error == "API请求失败";
        lock (_rowWriteLock)
        {
            if (failed) stats.FailCount++;
            else stats.SuccessCount++;
            stats.TotalCount++;
        }

        if (failed)
            AddLog($"❌ 第{rowIndex + 1}行：{userValue} 查询失败 - {error}", "Red");
        else
            AddLog($"✅ 第{rowIndex + 1}行：{userValue} 查询成功 - 金额：{amount} | 类型：{bizType}", "Green");
    }

    // ──────────────────────────────────────────────────────────────
    // 高级模式（模式4/5）行处理
    // ──────────────────────────────────────────────────────────────

    private async Task ProcessAdvancedRowAsync(DataRow row, int rowIndex, QueryStats stats)
    {
        // 模式4/5 从"消费ID"列读取厅ID或主播ID
        var id = SafeGetColumn(row, "消费ID");
        if (string.IsNullOrEmpty(id))
        {
            lock (_rowWriteLock)
            {
                stats.FailCount++;
                stats.TotalCount++;
            }
            AddLog($"❌ 第{rowIndex + 1}行：ID为空，请检查Excel列名（消费ID）", "Red");
            return;
        }

        var startTime = CustomStartTime;
        var endTime   = CustomEndTime;

        AddLog($"🔍 第{rowIndex + 1}行：查询 {id}  [{startTime} ~ {endTime}]", "Cyan");

        if (QueryMode == QueryMode.RoomSerialAndCreateTime)
        {
            // 模式4：查厅流水 + 开厅时间
            var (totalGold, serialErr) = await apiService.GetRoomSerialAsync(id, _currentToken!, startTime, endTime);
            var (createDate, createErr) = await apiService.GetGuildCreateTimeAsync(id, _currentToken!);

            // totalGold 除以100后取整展示
            var goldDisplay = string.IsNullOrEmpty(serialErr) ? (totalGold / 100).ToString() : serialErr;

            lock (_rowWriteLock)
            {
                SafeSetColumn(row, "查询开始时间", startTime);
                SafeSetColumn(row, "查询截止时间", endTime);
                SafeSetColumn(row, "厅流水",       goldDisplay);
                SafeSetColumn(row, "开厅时间",     string.IsNullOrEmpty(createErr) ? createDate : createErr);
                bool ok = string.IsNullOrEmpty(serialErr) && string.IsNullOrEmpty(createErr);
                if (ok) stats.SuccessCount++;
                else stats.FailCount++;
                stats.TotalCount++;
            }

            bool hasError = !string.IsNullOrEmpty(serialErr) || !string.IsNullOrEmpty(createErr);
            if (hasError)
                AddLog($"⚠️ 第{rowIndex + 1}行：{id} 部分失败 厅流水:{serialErr} 开厅:{createErr}", "Orange");
            else
                AddLog($"✅ 第{rowIndex + 1}行：{id} 厅流水={totalGold / 100} 开厅={createDate}", "Green");
        }
        else
        {
            // 模式5：查主播流水 + 身份证号（脱敏展示）
            var (totalGoldNum, serialErr) = await apiService.GetAnchorSerialAsync(id, _currentToken!, startTime, endTime);
            var (idCard, cardErr)         = await apiService.GetUserIdCardAsync(id, _currentToken!);

            // totalGoldNum 除以100后取整展示
            var goldDisplay = string.IsNullOrEmpty(serialErr) ? (totalGoldNum / 100).ToString() : serialErr;

            lock (_rowWriteLock)
            {
                SafeSetColumn(row, "查询开始时间", startTime);
                SafeSetColumn(row, "查询截止时间", endTime);
                SafeSetColumn(row, "主播流水",     goldDisplay);
                SafeSetColumn(row, "身份证号",     string.IsNullOrEmpty(cardErr) ? idCard : cardErr);
                bool ok = string.IsNullOrEmpty(serialErr) && string.IsNullOrEmpty(cardErr);
                if (ok) stats.SuccessCount++;
                else stats.FailCount++;
                stats.TotalCount++;
            }

            bool hasError = !string.IsNullOrEmpty(serialErr) || !string.IsNullOrEmpty(cardErr);
            if (hasError)
                AddLog($"⚠️ 第{rowIndex + 1}行：{id} 部分失败 流水:{serialErr} 身份证:{cardErr}", "Orange");
            else
                AddLog($"✅ 第{rowIndex + 1}行：{id} 主播流水={totalGoldNum / 100} 身份证={idCard}", "Green");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 辅助方法
    // ──────────────────────────────────────────────────────────────

    /// <summary>根据当前查询模式确保所需结果列已存在于 DataTable 中</summary>
    private void EnsureColumnsExist()
    {
        string[] cols;

        if (QueryMode == QueryMode.RoomSerialAndCreateTime)
        {
            cols = ["查询开始时间", "查询截止时间", "厅流水", "开厅时间"];
        }
        else if (QueryMode == QueryMode.AnchorSerialAndIdCard)
        {
            cols = ["查询开始时间", "查询截止时间", "主播流水", "身份证号"];
        }
        else
        {
            // 消费模式1/2/3
            var list = new List<string> { "金额", "业务类型", "查询开始时间", "查询截止时间" };
            if (QueryMode != QueryMode.UidRechargeOrGift)
                list.InsertRange(0, ["消费UID", "注册日期"]);
            cols = [.. list];
        }

        foreach (var col in cols)
        {
            if (!_currentDataTable!.Columns.Contains(col))
                _currentDataTable.Columns.Add(col);
        }
    }

    /// <summary>
    /// 根据截止时间策略（DeadlineMode）计算消费查询的截止时间。<br/>
    /// 最新：取当前系统时间；7/15/30日：以拍走时间为第1天（含），向后累计对应天数的最后一秒。<br/>
    /// 例：7日 = 拍走时间 +6天 23:59:59，即包含拍走当天在内共7天。
    /// </summary>
    private string CalculateEndDate(DateTime startDate)
    {
        // 注意：偏移量 = 天数 - 1，因为 startDate 本身已是第1天（含）
        return DeadlineMode switch
        {
            DeadlineMode.Days7  => startDate.AddDays(6).ToString("yyyy-MM-dd 23:59:59"),
            DeadlineMode.Days15 => startDate.AddDays(14).ToString("yyyy-MM-dd 23:59:59"),
            DeadlineMode.Days30 => startDate.AddDays(29).ToString("yyyy-MM-dd 23:59:59"),
            _                   => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")  // 最新
        };
    }

    /// <summary>安全读取 DataRow 列值，列不存在时返回 null 而非抛出异常</summary>
    private static string? SafeGetColumn(DataRow row, string columnName)
    {
        try
        {
            if (row.Table.Columns.Contains(columnName))
                return row[columnName]?.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SafeGetColumn] 读取列「{columnName}」失败：{ex.Message}");
        }
        return null;
    }

    /// <summary>安全写入 DataRow 列值，列不存在时静默跳过</summary>
    private static void SafeSetColumn(DataRow row, string columnName, string value)
    {
        try
        {
            if (row.Table.Columns.Contains(columnName))
                row[columnName] = value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SafeSetColumn] 写入列「{columnName}」失败：{ex.Message}");
        }
    }

    private string GetModeName() => QueryMode switch
    {
        QueryMode.IdRechargeOrGift        => "ID查充值/送礼（取最大值）",
        QueryMode.IdGiftOnly              => "ID只查送礼",
        QueryMode.UidRechargeOrGift       => "UID查充值/送礼",
        QueryMode.RoomSerialAndCreateTime => "ID查厅流水&开厅时间",
        QueryMode.AnchorSerialAndIdCard   => "ID查主播流水&实名",
        _                                 => "未知"
    };

    private void AddLog(string message, string color)
    {
        var timestamp = DateTime.Now.ToString("[HH:mm:ss]");
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry { Message = $"{timestamp} {message}", Color = color });
        });
    }
}

public class QueryStats
{
    public int TotalCount  { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount   { get; set; }
}
