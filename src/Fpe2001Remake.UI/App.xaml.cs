using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Fpe2001Remake.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // 壳层数据源：P1 由 MainViewModel 装配真实服务（扫描/地址表/冻结）。
        // 全局异常兜底：记录日志并提示，避免无提示闪退（定位/滚动等 UI 路径的历史崩溃根因）。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "logs");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n{e.Exception.StackTrace}");
            MessageBox.Show(
                $"程序遇到未处理异常，已记录到：\n{log}\n\n{e.Exception.Message}",
                "FPE2001-Remake 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 记录失败也要给用户反馈
            MessageBox.Show($"程序遇到未处理异常：\n{e.Exception.Message}", "FPE2001-Remake 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        // 标记已处理：不崩溃退出，用户可继续或自行关闭
        e.Handled = true;
    }
}
