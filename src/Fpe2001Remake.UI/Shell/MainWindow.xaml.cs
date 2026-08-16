using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Fpe2001Remake.UI.ViewModels;

namespace Fpe2001Remake.UI.Shell;

public partial class MainWindow : Window
{
    private nint _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += (_, _) => UpdateMaximizeGlyph();
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    private MainViewModel? Main => DataContext as MainViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        Windows11WindowFrame.Apply(_windowHandle, GetWindowFrameColor(isActive: true));
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Windows11WindowFrame.SetBorderColor(_windowHandle, GetWindowFrameColor(isActive: true));
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Windows11WindowFrame.SetBorderColor(_windowHandle, GetWindowFrameColor(isActive: false));
    }

    private Color GetWindowFrameColor(bool isActive)
    {
        var resourceKey = isActive ? "WindowFrameBrush" : "LineBrush";
        if (TryFindResource(resourceKey) is SolidColorBrush brush)
            return brush.Color;

        return isActive ? Color.FromRgb(0x75, 0x86, 0x9A) : Color.FromRgb(0xAA, 0xB6, 0xC4);
    }

    // ---------- 窗口按钮 ----------

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SystemCommands.CloseWindow(this);
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is null) return;
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (Main is { } main)
        {
            await main.DisposeAsync();
        }
        base.OnClosed(e);
    }

    // ---------- 模块带 ----------

    private void ModuleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: ModuleViewModelBase vm } && Main is { } main)
        {
            main.CurrentModule = vm;
        }
    }

    // ---------- 快捷键：Ctrl+1…9 切换模块；Ctrl+Shift+F12 全局停止 ----------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (Main is not { } main) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (shift && e.Key == Key.F12)
            {
                main.StopAll();
                e.Handled = true;
                return;
            }

            if (!shift && e.Key is >= Key.D1 and <= Key.D9)
            {
                main.ActivateModule(e.Key - Key.D1 + 1);
                e.Handled = true;
                return;
            }

            if (!shift && e.Key is >= Key.NumPad1 and <= Key.NumPad9)
            {
                main.ActivateModule(e.Key - Key.NumPad1 + 1);
                e.Handled = true;
            }
        }
    }
}
