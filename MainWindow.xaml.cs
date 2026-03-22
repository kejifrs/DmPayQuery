using System;
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

    protected override void OnClosing(CancelEventArgs e)
    {
        MainWindow_Unloaded(this, null);
        base.OnClosing(e);
    }
}