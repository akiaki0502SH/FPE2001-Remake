using System.Windows;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.UI.Views;

public partial class ProcessOpenDialog : Window
{
    public ProcessOpenDialog()
    {
        InitializeComponent();
        PidBox.Focus();
    }

    public int ProcessId { get; private set; }

    public ulong BaseAddress { get; private set; }

    public ulong ViewLength { get; private set; } = 64UL * 1024 * 1024;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PidBox.Text.Trim(), out var pid) || pid <= 0)
        {
            MessageBox.Show(this, "请输入有效的进程 ID。", "FPE2001-Remake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ulong baseAddress = 0;
        if (!string.IsNullOrWhiteSpace(BaseBox.Text) &&
            !AddressFormatting.TryParseHex(BaseBox.Text, out baseAddress))
        {
            MessageBox.Show(this, "基地址必须是十六进制（如 0x7FF600000000）。", "FPE2001-Remake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProcessId = pid;
        BaseAddress = baseAddress;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
