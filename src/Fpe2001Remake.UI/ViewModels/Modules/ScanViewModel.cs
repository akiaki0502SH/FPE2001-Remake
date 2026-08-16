using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>
/// 目标程序项（UI 展示）。同名进程合并为一行，但保留该程序的全部 PID，
/// 以便扫描时仍能精确绑定到一个进程实例。
/// </summary>
public sealed record TargetItem(string ProcessName, IReadOnlyList<int> ProcessIds)
{
    /// <summary>默认实例 PID；单实例或未选择实例时作为兼容回退值。</summary>
    public int ProcessId => ProcessIds.Count > 0 ? ProcessIds[0] : 0;

    /// <summary>实例选择器使用的 PID 列表。</summary>
    public IReadOnlyList<int> Instances => ProcessIds;

    public int InstanceCount => ProcessIds.Count;

    public bool HasMultipleInstances => ProcessIds.Count > 1;

    /// <summary>主列表只显示程序名，避免同一程序多进程造成列表膨胀。</summary>
    public string Display => ProcessName;

    public string InstanceSummary => HasMultipleInstances
        ? $"{InstanceCount} 个实例"
        : $"PID {ProcessId}";
}

/// <summary>候选行（UI 展示）。</summary>
public sealed record CandidateRow(
    ulong Address,
    string AddressText,
    string TypeText,
    byte Width,
    string CurrentValue,
    string PreviousValue,
    string HostEvidence)
{
    public LogicalAddress ToLogicalAddress(int pid, Endianness endianness, byte width)
        => new(AddressSpace.HostVirtual, $"pid:{pid}", Address, endianness, width);
}

/// <summary>
/// M01 扫描工作区（原版 TabScan；规格 §4）：
/// 目标选择 → 首次/再次扫描（8/16/32/64/浮点/文本）→ 候选列表 → 添加/编辑 → 送入地址表/编辑器。
/// </summary>
public sealed class ScanViewModel : ModuleViewModelBase
{
    private readonly IScanEngine _engine;
    private readonly IAddressBookService _addressBook;
    private readonly IScanApplicationService _targets;
    private readonly IFreezeService _freeze;
    private readonly Action<int, ulong>? _openInEditor;

    private TargetItem? _selectedTarget;
    private int _selectedProcessId;
    private string _selectedDataType = "自动";
    private string _selectedComparison = "等于";
    private string _valueText = "";
    private bool _isScanning;
    private Guid? _sessionId;
    private string _progressText = "";
    private CandidateRow? _selectedCandidate;
    private string _missionName = "未命名任务";
    private bool _candidateFreezeRunning;
    private AddressBookEntry? _frozenCandidateEntry;

    public ScanViewModel(
        IScanApplicationService targets,
        IScanEngine engine,
        IAddressBookService addressBook,
        IFreezeService freeze,
        Action<int, ulong>? openInEditor = null)
        : base(ModuleCatalog.Scan)
    {
        _targets = targets;
        _engine = engine;
        _addressBook = addressBook;
        _freeze = freeze;
        _openInEditor = openInEditor;

        Targets = [];
        Candidates = [];

        Actions.Add(new ActionItem("清空", null, "\uE894", IsEnabled: true, Command: new RelayCommand(_ => ClearResults())));
        Actions.Add(new ActionItem("新任务", "F2", "\uE710", IsEnabled: true, Command: new RelayCommand(_ => NewMission())));
        Actions.Add(new ActionItem("命名", "F3", "\uE8AC", IsEnabled: false, ToolTip: "P4 阶段实现"));
        Actions.Add(new ActionItem("加载 Mission", "F4", "\uE8E5", IsEnabled: false, ToolTip: "P3 阶段实现"));
        Actions.Add(new ActionItem("保存 Mission", "F5", "\uE74E", IsEnabled: false, ToolTip: "P3 阶段实现"));
        Actions.Add(new ActionItem("计算", "F7", "\uE8EF", IsEnabled: false, ToolTip: "P2 阶段实现"));
        Actions.Add(new ActionItem("撤销", "F8", "\uE7A7", IsEnabled: false, ToolTip: "P2 阶段实现"));
        Actions.Add(new ActionItem("扫描", "F10", "\uE768", Command: new RelayCommand(_ => _ = ScanAsync())));
        Actions.Add(new ActionItem("停止", "F11", "\uE71A", IsDanger: true, IsEnabled: false, Command: new RelayCommand(_ => _ = StopAsync())));

        RefreshTargetsCommand = new RelayCommand(_ => _ = RefreshTargetsAsync());
        // 扫描命令（ActionBar “扫描”/F10 与值输入框 Enter 共用）
        ScanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsScanning);
        AddToAddressTableCommand = new RelayCommand(_ => _ = AddToAddressTableAsync(), _ => SelectedCandidate is not null && !IsScanning);
        WriteCandidateCommand = new RelayCommand(_ => _ = WriteCandidateValueAsync(), _ => SelectedCandidate is not null && !IsScanning);
        FreezeCandidateCommand = new RelayCommand(_ => _ = ToggleFreezeCandidateAsync(), _ => SelectedCandidate is not null && !IsScanning);
        OpenInEditorCommand = new RelayCommand(_ => OpenCandidateInEditor(), _ => SelectedCandidate is not null);

        State = PageState.Ready;
        StateDetail = "选择目标进程后输入值开始扫描。";

        _ = RefreshTargetsAsync();
    }

    public ObservableCollection<TargetItem> Targets { get; }

    public TargetItem? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                var nextPid = value?.ProcessId ?? 0;
                SetProperty(ref _selectedProcessId, nextPid, nameof(SelectedProcessId));
                NotifyTargetChanged();
            }
        }
    }

    /// <summary>当前选中实例 PID；同名多进程时由实例选择器更新。</summary>
    public int SelectedProcessId
    {
        get => _selectedProcessId;
        set
        {
            if (_selectedTarget is null || !_selectedTarget.ProcessIds.Contains(value))
            {
                return;
            }

            if (SetProperty(ref _selectedProcessId, value))
            {
                NotifyTargetChanged();
            }
        }
    }

    /// <summary>目标变化（标题栏/状态栏联动），携带程序项和精确 PID。</summary>
    public event Action<TargetItem, int>? TargetChanged;

    /// <summary>数据类型（显示文本 → ScanDataType 映射由扫描逻辑处理）。自动 = 8/16/32 位同时扫描。</summary>
    public IReadOnlyList<string> DataTypes { get; } =
        ["自动", "8 位", "16 位", "32 位", "64 位", "浮点", "文本"];

    public string SelectedDataType
    {
        get => _selectedDataType;
        set => SetProperty(ref _selectedDataType, value);
    }

    public IReadOnlyList<string> Comparisons { get; } =
        ["等于", "不等于", "大于", "小于", "变化", "未变", "未知值"];

    public string SelectedComparison
    {
        get => _selectedComparison;
        set => SetProperty(ref _selectedComparison, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public string MissionName
    {
        get => _missionName;
        set => SetProperty(ref _missionName, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                OnPropertyChanged(nameof(CanStartScan));
                UpdateScanActionStates();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ScanButtonText => _sessionId is null ? "首次扫描" : "再次扫描";

    public bool CanStartScan => !IsScanning;

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public ObservableCollection<CandidateRow> Candidates { get; }

    public CandidateRow? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
            {
                OnPropertyChanged(nameof(CanAddToAddressTable));
                // 切换候选时复位锁定按钮状态（其他候选的冻结在地址表管理）
                if (_frozenCandidateEntry is not null && value is not null &&
                    _frozenCandidateEntry.Address.Value != value.Address)
                {
                    CandidateFreezeRunning = false;
                }
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanAddToAddressTable => SelectedCandidate is not null;

    public ICommand RefreshTargetsCommand { get; }

    /// <summary>首次/再次扫描（值输入框按 Enter 也可触发）。</summary>
    public ICommand ScanCommand { get; }

    public ICommand AddToAddressTableCommand { get; }

    public ICommand WriteCandidateCommand { get; }

    public ICommand FreezeCandidateCommand { get; }

    public ICommand OpenInEditorCommand { get; }

    /// <summary>候选锁定状态（“锁定”按钮显示）。</summary>
    public bool CandidateFreezeRunning
    {
        get => _candidateFreezeRunning;
        set
        {
            if (SetProperty(ref _candidateFreezeRunning, value))
            {
                OnPropertyChanged(nameof(CandidateFreezeButtonText));
            }
        }
    }

    public string CandidateFreezeButtonText => CandidateFreezeRunning ? "停止锁定" : "锁定";

    public override string WorkspacePlaceholder => "M01 扫描工作区（P1 已实现）。";

    // ---------- 命令 ----------

    private async Task RefreshTargetsAsync()
    {
        var previousName = SelectedTarget?.ProcessName;
        var previousPid = SelectedProcessId;

        Targets.Clear();
        var list = await _targets.ListTargetsAsync(CancellationToken.None);
        var grouped = list
            .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TargetItem(
                group.Key,
                group.Select(item => item.ProcessId).Distinct().OrderBy(pid => pid).ToArray()))
            .ToList();

        foreach (var target in grouped)
        {
            Targets.Add(target);
        }

        // 刷新后尽量恢复用户当前的程序和 PID；如果 PID 已退出，则回退到该程序的第一个实例。
        var restored = previousName is null
            ? null
            : grouped.FirstOrDefault(item => string.Equals(item.ProcessName, previousName, StringComparison.OrdinalIgnoreCase));
        if (restored is not null)
        {
            SelectedTarget = restored;
            if (restored.ProcessIds.Contains(previousPid))
            {
                SelectedProcessId = previousPid;
            }
        }
        else if (SelectedTarget is not null)
        {
            SelectedTarget = null;
        }

        State = Targets.Count > 0 ? PageState.Ready : PageState.Empty;
        StateDetail = Targets.Count > 0
            ? $"发现 {Targets.Count} 个目标程序，共 {list.Count} 个进程（Windows 系统进程已隐藏）。"
            : "未发现可用目标程序；Windows 系统进程已隐藏，启动目标程序后点“刷新”。";
    }

    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }
        if (SelectedTarget is null || SelectedProcessId <= 0)
        {
            ProgressText = "请先选择目标程序和实例。";
            StateDetail = "请先选择目标程序和实例。";
            return;
        }

        try
        {
            IsScanning = true;
            ProgressText = "准备中…";

            if (_sessionId is null)
            {
                await RunFirstScanAsync();
            }
            else
            {
                await RunNextScanAsync();
            }
        }
        catch (Exception ex)
        {
            State = PageState.Failed;
            StateDetail = $"扫描失败：{ex.Message}";
            ProgressText = "";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task RunFirstScanAsync()
    {
        var target = SelectedTarget;
        var processId = SelectedProcessId;
        if (target is null || processId <= 0)
        {
            ProgressText = "请先选择目标程序和实例。";
            StateDetail = "请先选择目标程序和实例。";
            return;
        }

        var condition = BuildCondition(ScanStepKind.FirstScan);
        if (condition is null)
        {
            StateDetail = "请输入有效的扫描值。";
            return;
        }

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = $"已读 {p.ReadBytes / (1024 * 1024)} MB / 计划 {p.PlannedBytes / (1024 * 1024)} MB，候选 {p.Candidates}";
        });

        var session = await _engine.BeginFirstScanAsync(
            processId, MissionName, condition, null, null, progress, CancellationToken.None);

        _sessionId = session.Id;
        var statsText = FormatSnapshotStats(session);
        ProgressText = $"首次扫描完成：{session.CandidateCount:N0} 个候选（{statsText}）。";
        StateDetail = $"任务“{MissionName}”：{session.CandidateCount:N0} 个候选。"
            + "再次扫描（输入新值）可收敛出真实类型；候选列表“类型”列显示命中宽度。";
        State = session.CandidateCount > 0 ? PageState.Ready : PageState.Empty;
        await LoadCandidatesAsync();
    }

    private async Task RunNextScanAsync()
    {
        var condition = BuildCondition(ScanStepKind.NextScan);
        if (condition is null)
        {
            StateDetail = "请输入有效的再次扫描条件。";
            return;
        }

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = $"再次扫描中… 候选 {p.Candidates}";
        });

        var session = await _engine.RunNextScanAsync(
            _sessionId!.Value, condition, progress, CancellationToken.None);

        ProgressText = $"再次扫描完成：{session.CandidateCount:N0} 个候选（{FormatSnapshotStats(session)}）。";
        State = session.CandidateCount > 0 ? PageState.Ready : PageState.Empty;
        StateDetail = session.CandidateCount > 0
            ? $"{session.CandidateCount:N0} 个候选。候选列表“类型”列显示命中宽度，选中可添加到地址表。"
            : "0 个候选。常见原因：① 数值在内存中以其他类型/编码存储（可改试“8 位”或“16 位”，或用十六进制输入）；"
              + "② 游戏数值在两次扫描之间又变化了（建议暂停游戏再扫描）；"
              + "③ 选错了进程实例（同名程序多实例时，确认目标 PID 是游戏所在进程）。";
        await LoadCandidatesAsync();
    }

    /// <summary>各类型快照候选数（自动模式如：8 位 1,120,000 / 16 位 2,900 / 32 位 300）。</summary>
    private static string FormatSnapshotStats(ScanSession session)
    {
        if (session.SnapshotStats is null || session.SnapshotStats.Count == 0)
        {
            return "无快照信息";
        }
        return string.Join(" / ", session.SnapshotStats
            .OrderByDescending(s => s.Count)
            .Select(s => $"{WidthText(DataTypeWidth(s.DataType))} {s.Count:N0}"));
    }

    private static byte DataTypeWidth(ScanDataType type) => type switch
    {
        ScanDataType.UInt8 => 1,
        ScanDataType.UInt16 => 2,
        ScanDataType.UInt32 => 4,
        ScanDataType.UInt64 => 8,
        ScanDataType.Float32 => 4,
        ScanDataType.Float64 => 8,
        _ => 4,
    };

    private async Task StopAsync()
    {
        if (_sessionId is null) return;
        await _engine.CancelAsync(_sessionId.Value, CancellationToken.None);
        ProgressText = "已停止（保留上个完整快照）";
        State = PageState.Cancelled;
    }

    private async Task LoadCandidatesAsync()
    {
        if (_sessionId is null) return;
        Candidates.Clear();
        var rows = await _engine.ReadCandidatesAsync(_sessionId.Value, 0, 500, CancellationToken.None);
        foreach (var c in rows)
        {
            var width = (byte)(c.Address.PointerWidthBits / 8);
            Candidates.Add(new CandidateRow(
                c.Address.Value,
                AddressFormatting.ToHex16(c.Address.Value),
                WidthText(width),
                width,
                c.CurrentValueText ?? "",
                c.PreviousValueText ?? "",
                c.HostEvidence ?? ""));
        }
    }

    /// <summary>宽度 → 类型显示文本（自动模式候选可能来自 8/16/32 位快照）。</summary>
    private static string WidthText(byte width) => width switch
    {
        1 => "8 位",
        2 => "16 位",
        4 => "32 位",
        8 => "64 位",
        _ => $"{width * 8} 位",
    };

    /// <summary>自动模式候选的宽度 → 整数类型（添加到地址表时使用真实命中类型）。</summary>
    private static ScanDataType WidthToDataType(byte width) => width switch
    {
        1 => ScanDataType.UInt8,
        2 => ScanDataType.UInt16,
        8 => ScanDataType.UInt64,
        _ => ScanDataType.UInt32,
    };

    private async Task AddToAddressTableAsync()
    {
        if (SelectedCandidate is null || SelectedTarget is null || SelectedProcessId <= 0 || _sessionId is null)
        {
            return;
        }

        var entry = CreateEntryForCandidate(SelectedCandidate);
        await _addressBook.UpsertAsync(entry, CancellationToken.None);
        StateDetail = $"已添加 {SelectedCandidate.AddressText}（{SelectedCandidate.TypeText}）到地址表。";
    }

    /// <summary>把候选转为地址表条目（自动模式用候选真实命中类型；手动模式用用户选择类型）。</summary>
    private AddressBookEntry CreateEntryForCandidate(CandidateRow candidate)
    {
        var dataType = SelectedDataType == "自动"
            ? WidthToDataType(candidate.Width)
            : MapDataType(SelectedDataType);
        var endianness = Endianness.LittleEndian;
        return new AddressBookEntry(
            Guid.NewGuid(),
            candidate.ToLogicalAddress(SelectedProcessId, endianness, (byte)(candidate.Width * 8)),
            $"候选 {candidate.AddressText}",
            "扫描结果",
            SelectedTarget!.ProcessName,
            null,
            null,
            dataType,
            endianness,
            null,
            true,
            candidate.CurrentValue,
            candidate.CurrentValue);
    }

    /// <summary>写入：修改选中候选的值（立即写内存，并加入地址表）。</summary>
    private async Task WriteCandidateValueAsync()
    {
        if (SelectedCandidate is null || SelectedTarget is null || SelectedProcessId <= 0)
        {
            return;
        }
        var row = SelectedCandidate;
        var dialog = new Fpe2001Remake.UI.Views.TextInputDialog(
            "写入候选值", $"新值（{row.TypeText}，当前 {row.CurrentValue}）", row.CurrentValue);
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var newValue = dialog.ValueText;
        if (newValue.Length == 0)
        {
            StateDetail = "请输入新值。";
            return;
        }

        var entry = CreateEntryForCandidate(row);
        await _addressBook.UpsertAsync(entry, CancellationToken.None);
        var ok = await _addressBook.WriteNowAsync(entry.Id, newValue, CancellationToken.None);
        StateDetail = ok
            ? $"已写入 {row.AddressText} = {newValue}（条目已加入地址表）。"
            : $"写入失败：{row.AddressText}（进程不可达或值无效）。";

        if (ok)
        {
            await _addressBook.RefreshValuesAsync(entry.Id, CancellationToken.None);
            var entries = await _addressBook.ListAsync(CancellationToken.None);
            var fresh = entries.FirstOrDefault(e => e.Id == entry.Id);
            if (fresh?.CurrentValueText is not null)
            {
                var idx = Candidates.IndexOf(row);
                if (idx >= 0)
                {
                    Candidates[idx] = row with { CurrentValue = fresh.CurrentValueText };
                }
            }
        }
    }

    /// <summary>锁定：持续把选中候选的值写回内存（冻结）；再点一次解除。</summary>
    private async Task ToggleFreezeCandidateAsync()
    {
        if (SelectedCandidate is null || SelectedTarget is null || SelectedProcessId <= 0)
        {
            return;
        }
        var row = SelectedCandidate;

        if (_frozenCandidateEntry is not null && _frozenCandidateEntry.Address.Value == row.Address)
        {
            await _freeze.StopAsync(_frozenCandidateEntry.Id, CancellationToken.None);
            _frozenCandidateEntry = null;
            CandidateFreezeRunning = false;
            StateDetail = $"已解除锁定 {row.AddressText}。";
            return;
        }

        var entry = CreateEntryForCandidate(row);
        await _addressBook.UpsertAsync(entry, CancellationToken.None);
        if (!ulong.TryParse(row.CurrentValue, out var target))
        {
            StateDetail = $"锁定失败：{row.AddressText} 当前值无法解析为整数。";
            return;
        }
        try
        {
            var status = await _freeze.StartAsync(new FreezeSpec(
                entry.Id, FreezeConditionKind.Always, target, null, null,
                TimeSpan.FromMilliseconds(250), 5), CancellationToken.None);
            _frozenCandidateEntry = status.Running ? entry : null;
            CandidateFreezeRunning = status.Running;
            StateDetail = status.Running
                ? $"已锁定 {row.AddressText} = {row.CurrentValue}（持续写回中；条目已加入地址表）。"
                : $"锁定失败：{row.AddressText}。";
        }
        catch (InvalidOperationException ex)
        {
            StateDetail = $"锁定失败：{ex.Message}";
        }
    }

    /// <summary>在十六进制编辑器中打开选中候选的地址（跳转 M03 并定位）。</summary>
    private void OpenCandidateInEditor()
    {
        if (SelectedCandidate is null || SelectedProcessId <= 0 || _openInEditor is null)
        {
            return;
        }
        _openInEditor(SelectedProcessId, SelectedCandidate.Address);
        StateDetail = $"已在十六进制编辑器打开 {SelectedCandidate.AddressText}（PID {SelectedProcessId}）。";
    }

    private void NotifyTargetChanged()
    {
        if (_selectedTarget is not null && _selectedProcessId > 0)
        {
            TargetChanged?.Invoke(_selectedTarget, _selectedProcessId);
        }
    }

    private void ClearResults()
    {
        Candidates.Clear();
        _sessionId = null;
        ProgressText = "";
        State = PageState.Ready;
        StateDetail = "已清空；可开始新任务。";
    }

    private void NewMission()
    {
        ClearResults();
        MissionName = $"任务 {DateTime.Now:HHmmss}";
        StateDetail = $"新任务“{MissionName}”已创建。";
    }

    private void UpdateScanActionStates()
    {
        for (var i = 0; i < Actions.Count; i++)
        {
            var action = Actions[i];
            if (action.Title == "扫描")
            {
                Actions[i] = action with { IsEnabled = !IsScanning, ToolTip = IsScanning ? "扫描进行中" : "开始扫描" };
            }
            else if (action.Title == "停止")
            {
                Actions[i] = action with { IsEnabled = IsScanning };
            }
        }
    }

    // ---------- 条件构造 ----------

    private ScanCondition? BuildCondition(ScanStepKind step)
    {
        var comparison = MapComparison(SelectedComparison);
        var dataType = MapDataType(SelectedDataType);

        if (comparison == ScanComparison.UnknownValue)
        {
            return new ScanCondition(step, comparison, null);
        }

        var valueText = ValueText.Trim();
        if (valueText.Length == 0 && comparison is ScanComparison.Equal or ScanComparison.NotEqual or ScanComparison.GreaterThan or ScanComparison.LessThan)
        {
            return null;
        }

        return new ScanCondition(step, comparison,
            new ScanValueSpec(dataType, valueText.Length == 0 ? null : valueText));
    }

    private static ScanComparison MapComparison(string text) => text switch
    {
        "等于" => ScanComparison.Equal,
        "不等于" => ScanComparison.NotEqual,
        "大于" => ScanComparison.GreaterThan,
        "小于" => ScanComparison.LessThan,
        "变化" => ScanComparison.Changed,
        "未变" => ScanComparison.Unchanged,
        "未知值" => ScanComparison.UnknownValue,
        _ => ScanComparison.Equal,
    };

    private static ScanDataType MapDataType(string text) => text switch
    {
        "自动" => ScanDataType.Auto,
        "8 位" => ScanDataType.UInt8,
        "16 位" => ScanDataType.UInt16,
        "32 位" => ScanDataType.UInt32,
        "64 位" => ScanDataType.UInt64,
        "浮点" => ScanDataType.Float32,
        "文本" => ScanDataType.Text,
        _ => ScanDataType.UInt32,
    };

    private static byte WidthOf(ScanDataType type) => type switch
    {
        ScanDataType.UInt8 => 8,
        ScanDataType.UInt16 => 16,
        ScanDataType.UInt32 => 32,
        ScanDataType.UInt64 => 64,
        ScanDataType.Float32 => 32,
        ScanDataType.Float64 => 64,
        ScanDataType.Auto => 32, // 自动模式不直接使用；候选行携带真实宽度
        _ => 32,
    };
}
