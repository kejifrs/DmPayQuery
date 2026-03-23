using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmPayQuery.Services;

namespace DmPayQuery.ViewModels;

public partial class LoginDialogViewModel(IApiService apiService) : ObservableObject
{
    [ObservableProperty]
    public partial string Account { get; set; } = string.Empty;

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
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CountdownText { get; set; } = "获取验证码";

    [ObservableProperty]
    public partial bool CanGetCode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    public string Token { get; private set; } = string.Empty;

    [RelayCommand]
    private async Task GetCodeAsync()
    {
        if (string.IsNullOrEmpty(Account) || string.IsNullOrEmpty(Password))
        {
            MessageBox.Show("请先填写账号和密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (success, message) = await apiService.GetVerificationCodeAsync(Account, Password);

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

        var (success, token, message) = await apiService.LoginAsync(Account, Password, Code);

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