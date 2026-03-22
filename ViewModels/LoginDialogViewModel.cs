using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmPayQuery.Services;

namespace DmPayQuery.ViewModels;

public partial class LoginDialogViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private string _account = string.Empty;

    // Password 需要手动实现，因为 PasswordBox 不支持绑定
    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _countdownText = "获取验证码";

    [ObservableProperty]
    private bool _canGetCode = true;

    [ObservableProperty]
    private bool _isLoggedIn;

    public string Token { get; private set; } = string.Empty;

    public LoginDialogViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [RelayCommand]
    private async Task GetCodeAsync()
    {
        if (string.IsNullOrEmpty(Account) || string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("请先填写账号和密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (success, message) = await _apiService.GetVerificationCodeAsync(Account, Password);

        if (!success)
        {
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);

        // 开始倒计时 - 使用 Dispatcher 更新 UI
        CanGetCode = false;

        for (int i = 60; i > 0; i--)
        {
            // 在主线程更新 UI
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CountdownText = $"{i}s后重发";
            });

            await Task.Delay(1000);
        }

        // 倒计时结束，恢复按钮
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CountdownText = "获取验证码";
            CanGetCode = true;
        });
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrEmpty(Account) || string.IsNullOrEmpty(Password) || string.IsNullOrEmpty(Code))
        {
            MessageBox.Show("账号/密码/验证码不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (success, token, message) = await _apiService.LoginAsync(Account, Password, Code);

        if (success)
        {
            Token = token;
            IsLoggedIn = true;
        }
        else
        {
            MessageBox.Show(message, "登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}