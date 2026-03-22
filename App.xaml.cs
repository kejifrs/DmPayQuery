using System.Windows;
using System;
using System.Windows.Markup;
using System.IO;

namespace DmPayQuery;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var main = new MainWindow();
            main.Show();
        }
        catch (XamlParseException xpe)
        {
            var msg = $"XAML 解析错误: {xpe.Message}\n行: {xpe.LineNumber}, 列: {xpe.LinePosition}\n内层异常: {xpe.InnerException?.Message}";
            MessageBox.Show(msg, "应用启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动 MainWindow 时发生异常: {ex.Message}", "应用启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}