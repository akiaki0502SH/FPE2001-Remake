using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Fpe2001Remake.UI.ViewModels;

/// <summary>动作栏条目（UIUX 规格 §3：文字、图标、F 键提示、危险动作分组）。</summary>
public sealed record ActionItem(
    string Title,
    string? ShortcutHint,
    string? IconGlyph,
    bool IsDanger = false,
    bool IsEnabled = true,
    ICommand? Command = null,
    string? ToolTip = null);

/// <summary>
/// 模块 ViewModel 基类：每个模块具备页面状态（规格 §16）与动作栏项（规格 §3）。
/// </summary>
public abstract class ModuleViewModelBase : ViewModelBase
{
    private PageState _state = PageState.Loading;
    private string _stateDetail = "";
    private bool _isActive;

    protected ModuleViewModelBase(ModuleDefinition definition)
    {
        Definition = definition;
    }

    public ModuleDefinition Definition { get; }

    public ObservableCollection<ActionItem> Actions { get; } = [];

    /// <summary>壳层导航选中态；由 MainViewModel 统一维护。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public PageState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateBrushKey));
            }
        }
    }

    public string StateDetail
    {
        get => _stateDetail;
        set => SetProperty(ref _stateDetail, value);
    }

    public string StateText => _state.ToDisplay();

    public string StateBrushKey => _state switch
    {
        PageState.Running => "PrimaryBrush",
        PageState.Failed or PageState.Disconnected => "DangerBrush",
        PageState.Cancelled or PageState.Unavailable => "MutedBrush",
        PageState.Ready or PageState.Empty => "SuccessBrush",
        _ => "MutedBrush",
    };

    /// <summary>模块页内容区描述（P0 占位；后续阶段由真实工作区替换）。</summary>
    public abstract string WorkspacePlaceholder { get; }
}
