using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpe2001Remake.AddressBook;
using Fpe2001Remake.Application;
using Fpe2001Remake.Automation;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.FileScan;
using Fpe2001Remake.Media;
using Fpe2001Remake.Scan;
using Fpe2001Remake.UI.ViewModels.Modules;

namespace Fpe2001Remake.UI.ViewModels;

/// <summary>
/// 应用壳层主视图模型（UIUX 规格 §2）：
/// 九模块导航（Ctrl+1…9）、标题栏目标/状态、状态栏、全局急停（Ctrl+Shift+F12）。
/// P1：装配真实扫描/地址表/冻结服务。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private ModuleViewModelBase _currentModule;
    private string _currentTarget = "未选择目标";
    private string _targetDetail = "未附加目标进程";
    private bool _freezeRunning;
    private bool _macroRunning;
    private string _speedText = "1x";
    private string _statusSource = "无来源";
    private string _statusProgress = "";

    private readonly IFreezeService _freeze;
    private readonly IAutomationService _automation;
    private readonly SpeedViewModel _speed;

    public MainViewModel()
    {
        var engine = new ScanEngine();
        var addressBook = new AddressBookService();
        var freeze = new FreezeService(addressBook.GetEntryById, addressBook.GetTargetIdentity);
        var scanApp = new ScanApplicationService(engine);
        _freeze = freeze;
        var editor = new HexEditorViewModel();

        var scanVm = new ScanViewModel(scanApp, engine, addressBook, freeze, (pid, address) =>
        {
            // 从扫描候选跳转到十六进制编辑器：打开进程内存视图并定位到该地址
            const ulong window = 0x1000;
            var baseAddress = address - (address % window);
            _ = editor.OpenProcessAtCoreAsync(pid, baseAddress, window, address);
            ActivateModule(3);
        });
        scanVm.TargetChanged += (target, processId) =>
        {
            CurrentTarget = target.ProcessName;
            TargetDetail = target.HasMultipleInstances
                ? $"PID {processId} / {target.InstanceCount} 个实例"
                : $"PID {processId}";
            StatusSource = $"目标：{target.Display} (PID {processId})";
        };

        var fileScan = new FileScanService();
        var legacyImport = new LegacyImportService();
        var settingsService = new JsonSettingsService();
        var mediaWorkspace = new MediaWorkspace();
        var hotkeys = new HotkeyService();
        var automation = new AutomationService(hotkeys);
        _automation = automation;
        var macroVm = new MacroWorkspaceViewModel(automation, hotkeys);
        _ = macroVm.LoadMacrosAsync();
        var speedVm = new SpeedViewModel();
        _speed = speedVm;

        Modules =
        [
            scanVm,
            new AddressTableViewModel(addressBook, freeze),
            editor,
            new FileWorkspaceViewModel(fileScan, path =>
            {
                // 从文件页跳转到十六进制编辑器打开文件
                _ = editor.OpenFileCoreAsync(path);
                ActivateModule(3);
            }),
            new MediaWorkspaceViewModel(mediaWorkspace),
            macroVm,
            speedVm,
            new SettingsViewModel(settingsService, legacyImport),
            new AboutViewModel(),
        ];
        _currentModule = Modules[0];
        _currentModule.IsActive = true;
        StopAllCommand = new RelayCommand(_ => StopAll());
    }

    public ObservableCollection<ModuleViewModelBase> Modules { get; }

    public ModuleViewModelBase CurrentModule
    {
        get => _currentModule;
        set
        {
            if (ReferenceEquals(_currentModule, value)) return;
            _currentModule.IsActive = false;
            if (!SetProperty(ref _currentModule, value)) return;
            _currentModule.IsActive = true;
            OnPropertyChanged(nameof(CurrentModuleTitle));
            OnPropertyChanged(nameof(CurrentModuleShortcut));
        }
    }

    public string CurrentModuleTitle => CurrentModule.Definition.Title;

    public string CurrentModuleShortcut => CurrentModule.Definition.ShortcutText;

    /// <summary>标题栏：当前目标（进程/模拟器）。</summary>
    public string CurrentTarget
    {
        get => _currentTarget;
        set => SetProperty(ref _currentTarget, value);
    }

    public string TargetDetail
    {
        get => _targetDetail;
        set => SetProperty(ref _targetDetail, value);
    }

    /// <summary>标题栏/状态栏：冻结运行中。</summary>
    public bool FreezeRunning
    {
        get => _freezeRunning;
        set => SetProperty(ref _freezeRunning, value);
    }

    /// <summary>标题栏/状态栏：宏运行中。</summary>
    public bool MacroRunning
    {
        get => _macroRunning;
        set => SetProperty(ref _macroRunning, value);
    }

    /// <summary>标题栏/状态栏：速度倍率。</summary>
    public string SpeedText
    {
        get => _speedText;
        set
        {
            if (SetProperty(ref _speedText, value))
            {
                OnPropertyChanged(nameof(SpeedNonDefault));
            }
        }
    }

    /// <summary>速度非 1x（规格 §1：持续状态）。</summary>
    public bool SpeedNonDefault => _speedText != "1x";

    /// <summary>状态栏：来源。</summary>
    public string StatusSource
    {
        get => _statusSource;
        set => SetProperty(ref _statusSource, value);
    }

    /// <summary>状态栏：任务进度。</summary>
    public string StatusProgress
    {
        get => _statusProgress;
        set => SetProperty(ref _statusProgress, value);
    }

    public ICommand StopAllCommand { get; }

    /// <summary>Ctrl+1…9 切换模块（顺序不变）。</summary>
    public void ActivateModule(int index)
    {
        if (index >= 1 && index <= Modules.Count)
        {
            CurrentModule = Modules[index - 1];
        }
    }

    /// <summary>全局急停：停止冻结、宏和速度临时覆盖（UIUX 规格 §13）。</summary>
    public void StopAll()
    {
        _ = StopAllAsync();
        FreezeRunning = false;
        MacroRunning = false;
        SpeedText = "1x";
        StatusProgress = "已停止（Ctrl+Shift+F12）";
    }

    private async Task StopAllAsync()
    {
        try
        {
            await Task.WhenAll(
                _freeze.StopAllAsync(CancellationToken.None).AsTask(),
                _automation.StopAllAsync(CancellationToken.None).AsTask(),
                _speed.StopAsync(CancellationToken.None).AsTask());
        }
        catch
        {
            StatusProgress = "急停已发出；部分服务未确认。";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _automation.StopAllAsync(CancellationToken.None);
        await _freeze.StopAllAsync(CancellationToken.None);
        await _speed.DisposeAsync();
    }
}
