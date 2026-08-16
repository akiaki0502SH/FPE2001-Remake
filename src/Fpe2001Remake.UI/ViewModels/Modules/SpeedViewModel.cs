using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Fpe2001Remake.Application;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>
/// M07 速度工作区（规格 §11）：仅通过适配器声明能力。
/// 无 SpeedControl 能力时页面保留但解释不可用（不尝试通用 DLL 钩子）。
/// </summary>
public sealed class SpeedViewModel : ModuleViewModelBase, IAsyncDisposable
{
    private readonly AdapterClient _client;
    private readonly string? _hostExePath;
    private readonly string _pipeName;

    private bool _connected;
    private string _adapterName = "未连接";
    private string _capabilitiesText = "—";
    private bool _hasSpeedControl;
    private bool _speedEnabled;
    private double _multiplier = 1.0;
    private string _statusText = "未连接适配器宿主。";
    private Process? _hostProcess;
    private bool _ownsHost;

    public SpeedViewModel(AdapterClient? client = null, string? hostExePath = null)
        : base(ModuleCatalog.Speed)
    {
        _pipeName = client?.PipeName ?? $"FPE2001-Remake-Adapter-{Guid.NewGuid():N}";
        _client = client ?? new AdapterClient(_pipeName);
        _hostExePath = hostExePath ?? FindHostExe();

        ConnectCommand = new RelayCommand(_ => _ = ConnectAsync());
        ApplyMultiplierCommand = new RelayCommand(_ => _ = SetMultiplierAsync(_multiplier));
        SetMultiplier1xCommand = new RelayCommand(_ => _ = SetMultiplierAsync(1.0));
        SetMultiplier2xCommand = new RelayCommand(_ => _ = SetMultiplierAsync(2.0));
        SetMultiplierHalfCommand = new RelayCommand(_ => _ = SetMultiplierAsync(0.5));

        Actions.Add(new ActionItem("连接", "F1", "\uE8BB", IsEnabled: true, Command: ConnectCommand));
        Actions.Add(new ActionItem("2x", null, null, IsEnabled: true, Command: SetMultiplier2xCommand, ToolTip: "2 倍速"));
        Actions.Add(new ActionItem("1x", null, null, IsEnabled: true, Command: SetMultiplier1xCommand, ToolTip: "1 倍速"));
        Actions.Add(new ActionItem("0.5x", null, null, IsEnabled: true, Command: SetMultiplierHalfCommand, ToolTip: "半速"));

        State = PageState.Ready;
        StateDetail = "速度控制由适配器声明能力提供；无能力时不尝试通用 DLL 钩子。";

        _ = ConnectAsync();
    }

    public bool Connected
    {
        get => _connected;
        set => SetProperty(ref _connected, value);
    }

    public string AdapterName
    {
        get => _adapterName;
        set => SetProperty(ref _adapterName, value);
    }

    public string CapabilitiesText
    {
        get => _capabilitiesText;
        set => SetProperty(ref _capabilitiesText, value);
    }

    public bool HasSpeedControl
    {
        get => _hasSpeedControl;
        set => SetProperty(ref _hasSpeedControl, value);
    }

    public bool SpeedEnabled
    {
        get => _speedEnabled;
        set
        {
            if (SetProperty(ref _speedEnabled, value))
            {
                _ = SetEnabledAsync(value);
            }
        }
    }

    public double Multiplier
    {
        get => _multiplier;
        set
        {
            if (SetProperty(ref _multiplier, value))
            {
                OnPropertyChanged(nameof(MultiplierText));
            }
        }
    }

    public string MultiplierText => $"×{Multiplier:0.00}";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand ConnectCommand { get; }

    public ICommand ApplyMultiplierCommand { get; }

    public ICommand SetMultiplier1xCommand { get; }

    public ICommand SetMultiplier2xCommand { get; }

    public ICommand SetMultiplierHalfCommand { get; }

    public override string WorkspacePlaceholder => "M07 速度工作台（P5 已实现）。";

    private static string? FindHostExe()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Fpe2001Remake.AdapterHost.exe");
        if (File.Exists(local)) return local;
        // UI bin: src/Fpe2001Remake.UI/bin/Release/net10.0-windows/
        // 宿主 exe: src/Fpe2001Remake.AdapterHost/bin/Release/net10.0/
        var relative = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Fpe2001Remake.AdapterHost", "bin", "Release", "net10.0", "Fpe2001Remake.AdapterHost.exe"));
        return File.Exists(relative) ? relative : null;
    }

    private async Task ConnectAsync()
    {
        // 1. 确保宿主进程运行
        if (_hostExePath is not null &&
            (_hostProcess is null || _hostProcess.HasExited))
        {
            try
            {
                _hostProcess = Process.Start(new ProcessStartInfo(_hostExePath)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Arguments = $"--pipe \"{_pipeName}\"",
                });
                _ownsHost = _hostProcess is not null;
            }
            catch (Exception ex)
            {
                StatusText = $"无法启动适配器宿主：{ex.Message}";
                return;
            }
        }

        // 2. 连接 + hello
        var ok = await _client.ConnectAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        if (!ok)
        {
            Connected = false;
            StatusText = "连接适配器宿主失败（确保 AdapterHost 已启动）。";
            return;
        }

        try
        {
            var response = await _client.InvokeAsync(
                new AdapterMessage(AdapterProtocol.ProtocolVersion, "req-hello", Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow.AddSeconds(5), "nonce", "hello", null),
                CancellationToken.None);
            if (response.Method == "error" || string.IsNullOrEmpty(response.PayloadJson))
            {
                StatusText = "适配器返回错误。";
                return;
            }
            var info = System.Text.Json.JsonSerializer.Deserialize<AdapterHelloInfo>(response.PayloadJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info is null)
            {
                StatusText = "适配器信息无效。";
                return;
            }

            Connected = true;
            AdapterName = $"{info.EmulatorName} ({info.AdapterId})";
            CapabilitiesText = info.Capabilities.ToString();
            HasSpeedControl = info.Capabilities.HasFlag(AdapterCapabilities.SpeedControl);
            StatusText = HasSpeedControl
                ? $"已连接：{info.EmulatorName}，支持速度控制。"
                : $"已连接：{info.EmulatorName}，无速度控制能力（{info.Capabilities}）。";
            State = PageState.Ready;

            if (HasSpeedControl)
            {
                await RefreshSpeedAsync();
            }
        }
        catch (Exception ex)
        {
            Connected = false;
            StatusText = $"适配器通信失败：{ex.Message}";
        }
    }

    private async Task RefreshSpeedAsync()
    {
        try
        {
            var capability = new SpeedControlClient(_client);
            var state = await capability.GetAsync(CancellationToken.None);
            SpeedEnabled = state.Enabled;
            Multiplier = state.Multiplier;
        }
        catch (Exception ex)
        {
            StatusText = $"读取速度状态失败：{ex.Message}";
        }
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        try
        {
            var capability = new SpeedControlClient(_client);
            await capability.SetEnabledAsync(enabled, CancellationToken.None);
            StatusText = enabled ? "速度控制已启用" : "速度控制已禁用";
        }
        catch (Exception ex)
        {
            StatusText = $"设置失败：{ex.Message}";
        }
    }

    private async Task SetMultiplierAsync(double multiplier)
    {
        try
        {
            var capability = new SpeedControlClient(_client);
            var state = await capability.SetMultiplierAsync(multiplier, CancellationToken.None);
            Multiplier = state.Multiplier;
            StatusText = $"速度 {state.Multiplier:0.00}x";
        }
        catch (Exception ex)
        {
            StatusText = $"设置失败：{ex.Message}";
        }
    }

    public async ValueTask StopAsync(CancellationToken ct)
    {
        if (!Connected || !HasSpeedControl) return;
        var capability = new SpeedControlClient(_client);
        await capability.SetMultiplierAsync(1.0, ct);
        await capability.SetEnabledAsync(false, ct);
        Multiplier = 1.0;
        SpeedEnabled = false;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch
        {
            // 关闭流程不能因已崩溃的适配器而阻塞。
        }
        await _client.DisposeAsync();
        if (_ownsHost && _hostProcess is { HasExited: false })
        {
            try { _hostProcess.Kill(entireProcessTree: true); }
            catch { }
        }
        _hostProcess?.Dispose();
    }
}
