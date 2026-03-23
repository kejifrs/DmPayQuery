using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DmPayQuery.Services;
using DmPayQuery.ViewModels;

namespace DmPayQuery.Views;

public partial class LoginDialog : Window
{
    public LoginDialogViewModel ViewModel { get; }

    public LoginDialog(IApiService apiService)
    {
        InitializeComponent();
        ViewModel = new LoginDialogViewModel(apiService);
        DataContext = ViewModel;

        // 绑定密码框变化到 ViewModel
        PasswordBox.PasswordChanged += (s, e) =>
        {
            ViewModel.Password = PasswordBox.Password;
            // update placeholder visibility binding helper (PasswordTextBox used for binding visibility)
            PasswordTextBox.Text = PasswordBox.Password;
        };

        // 小眼睛按钮：切换明文/密文
        ShowPasswordBtn.Checked += (s, e) =>
        {
            // 显示明文
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ShowPasswordBtn.Content = "🙈";
        };

        ShowPasswordBtn.Unchecked += (s, e) =>
        {
            // 恢复密文
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            ShowPasswordBtn.Content = "👁";
        };

        // 关键：监听登录成功，自动关闭窗口
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsLoggedIn) && ViewModel.IsLoggedIn)
            {
                DialogResult = true;  // 设置对话框结果为成功
                Close();              // 关闭登录窗口
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Try to remove the close button from the window chrome since we have a Cancel button.
        // Wrap in try/catch to avoid crashing on platforms where the native APIs are unavailable
        // (some publishing scenarios or platform differences may cause DllNotFoundException).
        try
        {
            const int GWL_STYLE = -16;
            const int WS_SYSMENU = 0x00080000;

            var hwnd = new WindowInteropHelper(this).Handle;

            if (hwnd == IntPtr.Zero)
                return;

            if (IntPtr.Size == 8)
            {
                var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                style &= ~WS_SYSMENU;
                SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
            }
            else
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style &= ~WS_SYSMENU;
                SetWindowLong(hwnd, GWL_STYLE, style);
            }
        }
        catch (DllNotFoundException)
        {
            // Native user32 APIs not available in this environment — silently ignore to keep app runnable.
        }
        catch (EntryPointNotFoundException)
        {
            // API entry point missing on this platform — ignore.
        }
        catch (Exception)
        {
            // Any other error when attempting to modify window styles should not prevent the app from running.
        }
    }

    // P/Invoke helpers for 32/64-bit
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;  // 取消
        Close();
    }
}