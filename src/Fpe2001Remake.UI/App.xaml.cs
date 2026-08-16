namespace Fpe2001Remake.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // 壳层数据源：P1 由 MainViewModel 装配真实服务（扫描/地址表/冻结）。
    }
}
