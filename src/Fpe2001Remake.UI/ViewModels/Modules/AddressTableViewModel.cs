using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>地址表行（UI 展示）。</summary>
public sealed class AddressEntryRow : ViewModelBase
{
    private string _currentValue = "";
    private string _writeValue = "";
    private bool _freezeRunning;
    private string _freezeStateText = "";

    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string Group { get; init; }
    public required string AddressText { get; init; }
    public required string TypeText { get; init; }
    public required ScanDataType DataType { get; init; }
    public required Endianness Endianness { get; init; }
    public required ulong Address { get; init; }
    public required string DomainId { get; init; }

    public string CurrentValue
    {
        get => _currentValue;
        set => SetProperty(ref _currentValue, value);
    }

    public string WriteValue
    {
        get => _writeValue;
        set => SetProperty(ref _writeValue, value);
    }

    public bool FreezeRunning
    {
        get => _freezeRunning;
        set
        {
            if (SetProperty(ref _freezeRunning, value))
            {
                OnPropertyChanged(nameof(FreezeButtonText));
                OnPropertyChanged(nameof(FreezeStateText));
            }
        }
    }

    public string FreezeButtonText => FreezeRunning ? "停止冻结" : "冻结";

    public string FreezeStateText
    {
        get => _freezeStateText;
        set => SetProperty(ref _freezeStateText, value);
    }
}

/// <summary>
/// M02 地址表工作区（原版 TabTabs；规格 §5）：
/// 条目、立即写入、条件冻结、注释；双击/编辑跳转 M03（P2）。
/// </summary>
public sealed class AddressTableViewModel : ModuleViewModelBase
{
    private readonly IAddressBookService _addressBook;
    private readonly IFreezeService _freeze;
    private AddressEntryRow? _selectedRow;

    public AddressTableViewModel(IAddressBookService addressBook, IFreezeService freeze)
        : base(ModuleCatalog.Addresses)
    {
        _addressBook = addressBook;
        _freeze = freeze;

        Rows = [];
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        WriteNowCommand = new RelayCommand(_ => _ = WriteNowAsync(), _ => SelectedRow is not null);
        FreezeToggleCommand = new RelayCommand(_ => _ = ToggleFreezeAsync(), _ => SelectedRow is not null);
        RemoveCommand = new RelayCommand(_ => _ = RemoveAsync(), _ => SelectedRow is not null);
        ClearCommand = new RelayCommand(_ => _ = ClearAsync());

        Actions.Add(new ActionItem("添加", "F1", "\uE710", IsEnabled: false, ToolTip: "P1 从扫描候选添加"));
        Actions.Add(new ActionItem("删除", "F2", "\uE74D", IsDanger: true, Command: RemoveCommand));
        Actions.Add(new ActionItem("清空", "F3", "\uE894", IsDanger: true, Command: ClearCommand));
        Actions.Add(new ActionItem("修改", "F4", "\uE70F", IsEnabled: false, ToolTip: "P2 阶段实现"));
        Actions.Add(new ActionItem("编辑", "F5", "\uE70F", IsEnabled: false, ToolTip: "P2 跳转十六进制编辑器"));
        Actions.Add(new ActionItem("写入", "F6", "\uE74E", Command: WriteNowCommand));
        Actions.Add(new ActionItem("加载 .fpe", "F7", "\uE8E5", IsEnabled: false, ToolTip: "P3 阶段实现"));
        Actions.Add(new ActionItem("保存 .fpe", "F8", "\uE74E", IsEnabled: false, ToolTip: "P3 阶段实现"));

        State = PageState.Loading;
        StateDetail = "加载地址表…";
        _ = RefreshAsync();

        // 订阅条目变化（写入/值刷新）
        if (_addressBook is AddressBook.AddressBookService svc)
        {
            svc.EntryChanged += async _ => await RefreshAsync();
        }
    }

    public ObservableCollection<AddressEntryRow> Rows { get; }

    public AddressEntryRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => SelectedRow is not null;

    public ICommand RefreshCommand { get; }

    public ICommand WriteNowCommand { get; }

    public ICommand FreezeToggleCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand ClearCommand { get; }

    public override string WorkspacePlaceholder => "M02 地址表工作区（P1 已实现：写入/冻结）。";

    // ---------- 命令 ----------

    private async Task RefreshAsync()
    {
        var entries = await _addressBook.ListAsync(CancellationToken.None);
        var statuses = await _freeze.ListAsync(CancellationToken.None);
        var freezeById = statuses.ToDictionary(s => s.EntryId);

        var currentIds = Rows.Select(r => r.Id).ToHashSet();
        var entryById = entries.ToDictionary(e => e.Id);

        // 新增/更新
        foreach (var e in entries)
        {
            var existing = Rows.FirstOrDefault(r => r.Id == e.Id);
            var running = freezeById.ContainsKey(e.Id);
            if (existing is null)
            {
                Rows.Add(new AddressEntryRow
                {
                    Id = e.Id,
                    Label = e.Label,
                    Group = e.Group,
                    AddressText = AddressFormatting.ToHex16(e.Address.Value),
                    TypeText = TypeTextOf(e.DataType),
                    DataType = e.DataType,
                    Endianness = e.Endianness,
                    Address = e.Address.Value,
                    DomainId = e.Address.DomainId,
                    CurrentValue = e.CurrentValueText ?? "",
                    WriteValue = e.WriteValueText ?? "",
                    FreezeRunning = running,
                });
            }
            else
            {
                existing.CurrentValue = e.CurrentValueText ?? "";
                existing.WriteValue = e.WriteValueText ?? "";
                existing.FreezeRunning = running;
            }
        }

        // 删除已移除的
        var removed = Rows.Where(r => !entryById.ContainsKey(r.Id)).ToList();
        foreach (var r in removed)
        {
            Rows.Remove(r);
        }

        State = Rows.Count > 0 ? PageState.Ready : PageState.Empty;
        StateDetail = Rows.Count > 0
            ? $"共 {Rows.Count} 个条目；双击或“写入”立即写值；“冻结”锁定目标值。"
            : "地址表为空；从扫描页把候选添加到地址表。";
    }

    private async Task WriteNowAsync()
    {
        if (SelectedRow is null) return;
        var ok = await _addressBook.WriteNowAsync(SelectedRow.Id, SelectedRow.WriteValue, CancellationToken.None);
        SelectedRow.FreezeStateText = ok ? "写入成功" : "写入失败（进程不可达或值无效）";
        if (ok)
        {
            await _addressBook.RefreshValuesAsync(SelectedRow.Id, CancellationToken.None);
        }
    }

    private async Task ToggleFreezeAsync()
    {
        if (SelectedRow is null) return;

        if (SelectedRow.FreezeRunning)
        {
            await _freeze.StopAsync(SelectedRow.Id, CancellationToken.None);
            SelectedRow.FreezeRunning = false;
            SelectedRow.FreezeStateText = "已停止";
        }
        else
        {
            if (!ulong.TryParse(SelectedRow.WriteValue, out var target))
            {
                SelectedRow.FreezeStateText = "冻结失败：写入值无效";
                return;
            }
            try
            {
                var status = await _freeze.StartAsync(new FreezeSpec(
                    SelectedRow.Id, FreezeConditionKind.Always, target, null, null,
                    TimeSpan.FromMilliseconds(250), 5), CancellationToken.None);
                SelectedRow.FreezeRunning = status.Running;
                SelectedRow.FreezeStateText = status.Running ? "冻结中" : "冻结失败";
            }
            catch (InvalidOperationException ex)
            {
                SelectedRow.FreezeStateText = $"冻结失败：{ex.Message}";
            }
        }
    }

    private async Task RemoveAsync()
    {
        if (SelectedRow is null) return;
        await _freeze.StopAsync(SelectedRow.Id, CancellationToken.None);
        await _addressBook.RemoveAsync(SelectedRow.Id, CancellationToken.None);
        await RefreshAsync();
    }

    private async Task ClearAsync()
    {
        await _freeze.StopAllAsync(CancellationToken.None);
        await _addressBook.ClearAsync(CancellationToken.None);
        await RefreshAsync();
    }

    private static string TypeTextOf(ScanDataType type) => type switch
    {
        ScanDataType.UInt8 => "8 位",
        ScanDataType.UInt16 => "16 位",
        ScanDataType.UInt32 => "32 位",
        ScanDataType.UInt64 => "64 位",
        ScanDataType.Float32 => "浮点",
        ScanDataType.Float64 => "双精度",
        ScanDataType.Bytes => "字节串",
        ScanDataType.Text => "文本",
        _ => "未知",
    };
}
