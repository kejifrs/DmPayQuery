using System.Windows;
using System.Windows.Input;
using DmPayQuery.Services;
using DmPayQuery.ViewModels;

namespace DmPayQuery.Views;

public partial class LoginDialog : Window
{
    private bool _isSynchronizingPassword;

    public LoginDialogViewModel ViewModel { get; }

    public LoginDialog(IApiService apiService)
    {
        InitializeComponent();
        ViewModel = new LoginDialogViewModel(apiService);
        DataContext = ViewModel;

        // 绑定密码框变化到 ViewModel
        PasswordBox.PasswordChanged += (s, e) => SyncPasswordFromPasswordBox();
        PasswordTextBox.TextChanged += (s, e) => SyncPasswordFromTextBox();

        // 小眼睛按钮：切换明文/密文
        ShowPasswordBtn.Checked += (s, e) =>
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ShowPasswordBtn.Content = "🙈";
        };

        ShowPasswordBtn.Unchecked += (s, e) =>
        {
            PasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            ShowPasswordBtn.Content = "👁";
        };

        // 关键：监听登录成功，自动关闭窗口
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsLoggedIn) && ViewModel.IsLoggedIn)
            {
                ViewModel.CancelCountdown();
                DialogResult = true;
                Close();
            }
        };

        Closed += (s, e) => ViewModel.CancelCountdown();
    }

    private void SyncPasswordFromPasswordBox()
    {
        if (_isSynchronizingPassword)
            return;

        _isSynchronizingPassword = true;
        try
        {
            if (PasswordTextBox.Text != PasswordBox.Password)
                PasswordTextBox.Text = PasswordBox.Password;

            ViewModel.Password = PasswordBox.Password;
        }
        finally
        {
            _isSynchronizingPassword = false;
        }
    }

    private void SyncPasswordFromTextBox()
    {
        if (_isSynchronizingPassword)
            return;

        _isSynchronizingPassword = true;
        try
        {
            if (PasswordBox.Password != PasswordTextBox.Text)
                PasswordBox.Password = PasswordTextBox.Text;

            ViewModel.Password = PasswordTextBox.Text;
        }
        finally
        {
            _isSynchronizingPassword = false;
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCountdown();
        DialogResult = false;
        Close();
    }
}