using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>宏列表行。</summary>
public sealed class MacroRow
{
    public required Macro Macro { get; init; }

    public string Name => Macro.Name;

    public string Hotkey => Macro.Hotkey ?? "—";

    public string EnabledText => Macro.Enabled ? "启用" : "禁用";

    public int StepCount => Macro.Steps.Count;
}

/// <summary>宏步骤编辑行。</summary>
public sealed class MacroStepRow : ViewModelBase
{
    private StepActionKind _action;
    private string _keyCode = "";
    private string _mouseButton = "Left";
    private int _delayMs;
    private string _comment = "";

    public StepActionKind Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
            {
                OnPropertyChanged(nameof(UsesKey));
                OnPropertyChanged(nameof(UsesMouse));
            }
        }
    }

    public string KeyCode
    {
        get => _keyCode;
        set => SetProperty(ref _keyCode, value);
    }

    public string MouseButton
    {
        get => _mouseButton;
        set => SetProperty(ref _mouseButton, value);
    }

    public int DelayMs
    {
        get => _delayMs;
        set => SetProperty(ref _delayMs, value);
    }

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value);
    }

    public bool UsesKey => Action is StepActionKind.KeyDown or StepActionKind.KeyUp or StepActionKind.KeyPress;

    public bool UsesMouse => Action is StepActionKind.MouseLeftClick or StepActionKind.MouseRightClick
        or StepActionKind.MouseLeftDoubleClick or StepActionKind.MouseRightDoubleClick;

    public MacroStep ToStep() => new(Action, KeyCode, MouseButton, DelayMs, Action == StepActionKind.WaitForeground, Comment);
}

/// <summary>
/// M06 宏工作区（规格 §10）：宏 CRUD、步骤编辑、热键注册（RegisterHotKey）、运行/倒计时/急停。
/// </summary>
public sealed class MacroWorkspaceViewModel : ModuleViewModelBase
{
    private readonly IAutomationService _automation;
    private readonly IHotkeyService _hotkeys;

    private MacroRow? _selected;
    private string _editName = "";
    private string _editHotkey = "";
    private int _editRepeat = 1;
    private string _editWindowTitle = "";
    private bool _isRunning;
    private string _statusText = "";

    public MacroWorkspaceViewModel(IAutomationService? automation = null, IHotkeyService? hotkeys = null)
        : base(ModuleCatalog.Macro)
    {
        _automation = automation ?? new Fpe2001Remake.Automation.AutomationService();
        _hotkeys = hotkeys ?? new Fpe2001Remake.Automation.HotkeyService(OnHotkey);

        NewCommand = new RelayCommand(_ => NewMacro());
        SaveCommand = new RelayCommand(_ => _ = SaveAsync());
        DeleteCommand = new RelayCommand(_ => _ = DeleteAsync(), _ => Selected is not null);
        RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => Selected is not null && !IsRunning);
        StopAllCommand = new RelayCommand(_ => _ = StopAllAsync(), _ => IsRunning);
        AddStepCommand = new RelayCommand(_ => AddStep());
        RemoveStepCommand = new RelayCommand(_ => RemoveStep(), _ => SelectedStep is not null);

        Actions.Add(new ActionItem("新建宏", "F1", "\uE710", IsEnabled: true, Command: NewCommand));
        Actions.Add(new ActionItem("保存", "F2", "\uE74E", IsEnabled: true, Command: SaveCommand));
        Actions.Add(new ActionItem("删除", "F3", "\uE74D", Command: DeleteCommand));
        Actions.Add(new ActionItem("运行", "F4", "\uE768", Command: RunCommand, ToolTip: "执行所选宏"));
        Actions.Add(new ActionItem("急停", "F5", "\uE71A", Command: StopAllCommand, ToolTip: "立即停止全部宏"));

        Steps = [];
        State = PageState.Ready;
        StateDetail = "新建宏后编辑步骤；热键使用 RegisterHotKey（如 Ctrl+Alt+F9）。";
    }

    public ObservableCollection<MacroRow> Macros { get; } = [];

    public ObservableCollection<MacroStepRow> Steps { get; }

    public MacroRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadEditor(value);
                OnPropertyChanged(nameof(DeleteCommand));
                OnPropertyChanged(nameof(RunCommand));
            }
        }
    }

    public MacroStepRow? SelectedStep { get; set; }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditHotkey
    {
        get => _editHotkey;
        set => SetProperty(ref _editHotkey, value);
    }

    public int EditRepeat
    {
        get => _editRepeat;
        set => SetProperty(ref _editRepeat, value);
    }

    public string EditWindowTitle
    {
        get => _editWindowTitle;
        set => SetProperty(ref _editWindowTitle, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(RunCommand));
                OnPropertyChanged(nameof(StopAllCommand));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public IReadOnlyList<StepActionKind> ActionKinds { get; } =
        [StepActionKind.KeyPress, StepActionKind.KeyDown, StepActionKind.KeyUp,
         StepActionKind.MouseLeftClick, StepActionKind.MouseRightClick,
         StepActionKind.MouseLeftDoubleClick, StepActionKind.MouseRightDoubleClick,
         StepActionKind.Delay, StepActionKind.WaitForeground];

    public ICommand NewCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand RunCommand { get; }

    public ICommand StopAllCommand { get; }

    public ICommand AddStepCommand { get; }

    public ICommand RemoveStepCommand { get; }

    public override string WorkspacePlaceholder => "M06 宏工作台（P4 已实现）。";

    /// <summary>从服务加载宏列表（构造后由组合根调用）。</summary>
    public async Task LoadMacrosAsync()
    {
        Macros.Clear();
        var list = await _automation.ListAsync(CancellationToken.None);
        foreach (var m in list)
        {
            Macros.Add(new MacroRow { Macro = m });
            if (m.Enabled && m.Hotkey is { Length: > 0 })
            {
                await _hotkeys.RegisterAsync(m.Hotkey, m.Id, CancellationToken.None);
            }
        }
    }

    private void NewMacro()
    {
        var dialog = new Views.TextInputDialog("新建宏", "宏名称", "新宏");
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var macro = new Macro(Guid.NewGuid(), dialog.ValueText,
            new TargetBinding(null, null, null, null), null, [], 1, TimeSpan.FromMinutes(5), false);
        Macros.Add(new MacroRow { Macro = macro });
        Selected = Macros[^1];
        StatusText = $"已创建宏：{dialog.ValueText}";
    }

    private void LoadEditor(MacroRow? row)
    {
        Steps.Clear();
        if (row is null)
        {
            EditName = EditHotkey = "";
            EditRepeat = 1;
            EditWindowTitle = "";
            return;
        }
        var m = row.Macro;
        EditName = m.Name;
        EditHotkey = m.Hotkey ?? "";
        EditRepeat = Math.Max(1, m.Repeat);
        EditWindowTitle = m.Target.WindowTitlePattern ?? "";
        foreach (var step in m.Steps)
        {
            Steps.Add(new MacroStepRow
            {
                Action = step.Action,
                KeyCode = step.KeyCode ?? "",
                MouseButton = step.MouseButton ?? "Left",
                DelayMs = step.DelayMs,
                Comment = step.Comment ?? "",
            });
        }
    }

    private async Task SaveAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(EditName))
        {
            StatusText = "请选择宏并输入名称。";
            return;
        }

        var macro = new Macro(
            Selected.Macro.Id,
            EditName.Trim(),
            new TargetBinding(null, null, string.IsNullOrWhiteSpace(EditWindowTitle) ? null : EditWindowTitle, null),
            string.IsNullOrWhiteSpace(EditHotkey) ? null : EditHotkey.Trim(),
            Steps.Select(s => s.ToStep()).ToList(),
            Math.Max(1, EditRepeat),
            TimeSpan.FromMinutes(5),
            Enabled: !string.IsNullOrWhiteSpace(EditHotkey));

        var saved = await _automation.SaveAsync(macro, CancellationToken.None);

        // 热键注册
        await _hotkeys.UnregisterAsync(saved.Id, CancellationToken.None);
        if (saved.Enabled && saved.Hotkey is { Length: > 0 })
        {
            var ok = await _hotkeys.RegisterAsync(saved.Hotkey, saved.Id, CancellationToken.None);
            StatusText = ok ? $"已保存并注册热键 {saved.Hotkey}" : $"已保存；热键 {saved.Hotkey} 注册失败（可能被占用）";
        }
        else
        {
            StatusText = "已保存（无热键）";
        }

        var idx = Macros.IndexOf(Selected);
        if (idx >= 0)
        {
            Macros[idx] = new MacroRow { Macro = saved };
            Selected = Macros[idx];
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        await _automation.DeleteAsync(Selected.Macro.Id, CancellationToken.None);
        var removed = Selected;
        Macros.Remove(removed);
        Selected = null;
        StatusText = $"已删除宏：{removed.Name}";
    }

    private async Task RunAsync()
    {
        if (Selected is null) return;
        IsRunning = true;
        StatusText = $"运行宏：{Selected.Name}";
        try
        {
            var state = await _automation.RunAsync(Selected.Macro.Id, countdown: false, CancellationToken.None);
            StatusText = state.LastError is null
                ? $"宏完成：{Selected.Name}"
                : $"宏结束：{state.LastError}";
        }
        catch (Exception ex)
        {
            StatusText = $"运行失败：{ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task StopAllAsync()
    {
        StatusText = "急停：已停止全部宏";
        await _automation.StopAllAsync(CancellationToken.None);
    }

    private void AddStep()
    {
        Steps.Add(new MacroStepRow { Action = StepActionKind.KeyPress, KeyCode = "F1", DelayMs = 50 });
        StatusText = "已添加步骤";
    }

    private void RemoveStep()
    {
        if (SelectedStep is not null)
        {
            Steps.Remove(SelectedStep);
            SelectedStep = null;
        }
    }

    private void OnHotkey(Guid macroId)
    {
        // 热键触发：在 UI 线程执行宏
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            IsRunning = true;
            StatusText = "热键触发宏";
            try
            {
                var state = await _automation.RunAsync(macroId, countdown: false, CancellationToken.None);
                StatusText = state.LastError is null ? "宏完成" : $"宏结束：{state.LastError}";
            }
            finally
            {
                IsRunning = false;
            }
        });
    }
}
