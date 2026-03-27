using System.Windows;
using System.Windows.Input;
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
            PasswordTextBox.Text = PasswordBox.Password;
        };

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
                DialogResult = true;
                Close();
            }
        };
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}