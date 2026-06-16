using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DmPayQuery.Services;
using DmPayQuery.ViewModels;

namespace DmPayQuery;

public partial class MainWindow : Window
{
    private readonly NotifyCollectionChangedEventHandler? _logHandler;
    private readonly HttpClient? _httpClient;

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            _httpClient = new HttpClient();
            var apiService = new ApiService(_httpClient);
            var cacheService = new CacheService();
            var excelService = new ExcelService();

            var viewModel = new MainViewModel(apiService, cacheService, excelService);
            DataContext = viewModel;

            // 安全地订阅事件
            if (viewModel.Logs != null)
            {
                _logHandler = (s, e) =>
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => LogScrollViewer?.ScrollToEnd()));
                };
                viewModel.Logs.CollectionChanged += _logHandler;
            }

            // 清理资源
            Unloaded += MainWindow_Unloaded;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化失败: {ex.Message}", "错误",
                           MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void MainWindow_Unloaded(object sender, RoutedEventArgs? e)
    {
        if (DataContext is MainViewModel vm && vm.Logs != null && _logHandler != null)
            vm.Logs.CollectionChanged -= _logHandler;

        _httpClient?.Dispose();
    }

    private void RdoProfileDefault_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ProgressUpdateBatchSize = 10;
            vm.AnchorSerialPageSize = 100;
            vm.AdvancedConcurrency = 5;
        }
    }

    private void RdoProfileMedium_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ProgressUpdateBatchSize = 5;
            vm.AnchorSerialPageSize = 500;
            vm.AdvancedConcurrency = 8;
        }
    }

    private void RdoProfileHigh_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ProgressUpdateBatchSize = 2;
            vm.AnchorSerialPageSize = 1000;
            vm.AdvancedConcurrency = 12;
        }
    }

    private void AnchorOption_Checked(object sender, RoutedEventArgs e)
    {
        // 当任一主播选项被勾选时，把 QueryMode 切到 Anchor 模式
        if (DataContext is MainViewModel vm)
        {
            vm.QueryMode = DmPayQuery.Models.QueryMode.AnchorSerialAndIdCard;
        }
    }

    private void AnchorOption_Unchecked(object sender, RoutedEventArgs e)
    {
        // 当所有主播选项都未勾选时，不强制切换 QueryMode，仅保留当前选择
        if (DataContext is MainViewModel vm)
        {
            if (!ChkAnchorSerial.IsChecked.GetValueOrDefault() && !ChkAnchorIdCard.IsChecked.GetValueOrDefault() && !ChkAnchorAvatar.IsChecked.GetValueOrDefault())
            {
                // 如果希望此时自动切换回 Room 模式，可在此设置；当前保留不切换
            }
            else
            {
                vm.QueryMode = DmPayQuery.Models.QueryMode.AnchorSerialAndIdCard;
            }
        }
    }

    private void RoomMode_Checked(object sender, RoutedEventArgs e)
    {
        // 切换到厅流水模式时，清除 Anchor 的单选语义（不自动修改复选框的勾选状态），确保互斥逻辑正确
        if (DataContext is MainViewModel vm)
        {
            vm.QueryMode = DmPayQuery.Models.QueryMode.RoomSerialAndCreateTime;
            // 取消所有主播复选框的勾选（直接更新 UI 控件）
            ChkAnchorSerial.IsChecked = false;
            ChkAnchorIdCard.IsChecked = false;
            ChkAnchorAvatar.IsChecked = false;

            // 在下一帧确保 Radio 的 IsChecked 被设置（防止首次点击被用于取消复选框而未触发单选状态）
            Dispatcher.BeginInvoke(new Action(() => RdoRoom.IsChecked = true));
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        MainWindow_Unloaded(this, null);
        base.OnClosing(e);
    }
}