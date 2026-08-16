using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.ByteSources.File;
using Fpe2001Remake.ByteSources.Process;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.UI.Views;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>十六进制行（虚拟行：构造时按需从文档读取）。</summary>
public sealed class HexRow : ViewModelBase
{
    private readonly HexDocument _document;
    private string _hex = "";
    private string _text = "";
    private bool _isModified;
    private readonly byte[] _bytes = new byte[16];
    private readonly bool[] _available = new bool[16];

    public HexRow(HexDocument document, ulong offset)
    {
        _document = document;
        Offset = offset;
    }

    public ulong Offset { get; }

    public string OffsetText => AddressFormatting.ToHex16(Offset);

    /// <summary>按字节列索引取十六进制文本（XAML 16 个字节列绑定 {Binding [i]}）。不可读字节显示 ??。</summary>
    public string this[int columnIndex]
    {
        get
        {
            if (columnIndex < 0 || columnIndex >= 16)
            {
                return "";
            }
            return _available[columnIndex] ? _bytes[columnIndex].ToString("X2") : "??";
        }
    }

    public string Hex
    {
        get => _hex;
        private set => SetProperty(ref _hex, value);
    }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public bool IsModified
    {
        get => _isModified;
        set => SetProperty(ref _isModified, value);
    }

    /// <summary>重新从文档读取本行（虚拟化按需加载）。</summary>
    public void Refresh()
    {
        var data = new byte[16];
        var read = _document.ReadBytes(Offset, data);
        for (var i = 0; i < 16; i++)
        {
            _available[i] = i < read && _document.IsRangeAvailable(Offset + (ulong)i, 1);
            _bytes[i] = i < read ? data[i] : (byte)0;
        }
        for (var i = 0; i < 16; i++)
        {
            OnPropertyChanged($"{nameof(Hex)}_b{i}");
        }
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsModified));
    }
}

/// <summary>
/// 虚拟行集合：O(1) 索引，DataGrid 虚拟化按需取行。
/// 行实例带缓存：同一索引始终返回同一实例，保证 ScrollIntoView/选中引用一致
/// （否则每次 new 出新实例，DataGrid 找不到匹配项导致滚动定位失败）。
/// </summary>
public sealed class VirtualHexRows : IList<HexRow>
{
    private readonly HexDocument _document;
    private readonly ulong _rowCount;
    private readonly Dictionary<int, HexRow> _cache = [];

    /// <summary>缓存上限：超过即整体重建（DataGrid 渲染窗口远小于此，正常滚动不触发）。</summary>
    private const int MaxCacheEntries = 2048;

    public VirtualHexRows(HexDocument document)
    {
        _document = document;
        _rowCount = (document.Length + 15) / 16;
    }

    public HexRow this[int index]
    {
        get
        {
            if (_cache.TryGetValue(index, out var cached))
            {
                return cached;
            }
            var row = new HexRow(_document, (ulong)index * 16);
            row.Refresh();
            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear(); // 大面积滚动后重建；当前窗口会立即重新缓存
            }
            _cache[index] = row;
            return row;
        }
        set => throw new NotSupportedException();
    }

    public int Count => (int)Math.Min((ulong)int.MaxValue, _rowCount);

    public bool IsReadOnly => true;
    public void Add(HexRow item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(HexRow item) => throw new NotSupportedException();
    public void CopyTo(HexRow[] array, int arrayIndex) => throw new NotSupportedException();

    /// <summary>惰性枚举（按需索引）；DataGrid 虚拟化主要走 Count + 索引器。</summary>
    public IEnumerator<HexRow> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    public int IndexOf(HexRow item) => throw new NotSupportedException();
    public void Insert(int index, HexRow item) => throw new NotSupportedException();
    public bool Remove(HexRow item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// M03 十六进制编辑工作区（规格 §6）：File / ProcessMemory 双来源；
/// 网格虚拟化、覆盖编辑、撤销、查找、书签、保存/提交。
/// </summary>
public sealed class HexEditorViewModel : ModuleViewModelBase
{
    private readonly IByteSourceFactory? _factory;
    private readonly IHexEditorService? _editor;

    private HexDocument? _document;
    private IEditableByteSource? _editableSource;
    private VirtualHexRows? _rows;
    private bool _editMode;
    private string _statusText = "未打开来源";
    private string _encoding = "ASCII";
    private string _findText = "";
    private string _editValue = "";
    private HexRow? _selectedRow;
    private string _sourceLabel = "";
    private string? _openedFilePath;
    private ulong? _selectedByteOffset;
    private ulong? _pendingFocusOffset;

    // 进程视图参数（编辑提交成功后刷新视图用）
    private int _openedPid;
    private ulong _openedBaseAddress;
    private ulong _openedViewLength;
    private ulong _openedFocusOffset;
    private bool _hasOpenedProcess;

    /// <summary>最近一次定位请求的目标字节偏移（页面未就绪时缓存，View 就绪后消费）。</summary>
    public ulong? PendingFocusOffset => _pendingFocusOffset;

    /// <summary>当前选中的字节偏移（字节级编辑起点）。</summary>
    public ulong? SelectedByteOffset
    {
        get => _selectedByteOffset;
        private set => SetProperty(ref _selectedByteOffset, value);
    }

    /// <summary>最近定位/选中的字节在行内的列索引（0-15），View 定位单元格用。</summary>
    public int FocusByteColumn { get; private set; }

    public HexEditorViewModel(IByteSourceFactory? factory = null, IHexEditorService? editor = null)
        : base(ModuleCatalog.Editor)
    {
        _factory = factory;
        _editor = editor;

        OpenFileCommand = new RelayCommand(_ => _ = OpenFileAsync());
        OpenProcessCommand = new RelayCommand(_ => _ = OpenProcessAsync());
        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => CanSave);
        UndoCommand = new RelayCommand(_ => Undo(), _ => CanUndo);
        FindCommand = new RelayCommand(_ => _ = FindAsync());
        ApplyEditCommand = new RelayCommand(_ => _ = ApplyEditAsync(), _ => CanApplyEdit);
        ToggleEditModeCommand = new RelayCommand(_ => ToggleEditMode());

        Actions.Add(new ActionItem("打开", "F1", "\uE8E5", IsEnabled: true, Command: OpenFileCommand, ToolTip: "打开文件到十六进制视图（默认可编辑，编辑模式需手动开启）"));
        Actions.Add(new ActionItem("打开进程", null, "\uE8E5", IsEnabled: true, Command: OpenProcessCommand, ToolTip: "按 PID 打开进程内存视图"));
        Actions.Add(new ActionItem("保存", null, "\uE74E", Command: SaveCommand, ToolTip: "原子提交（备份+替换）"));
        Actions.Add(new ActionItem("查找", "F2", "\uE721", IsEnabled: true, Command: FindCommand));
        Actions.Add(new ActionItem("撤销", "F3", "\uE7A7", Command: UndoCommand));
        Actions.Add(new ActionItem("书签", "F4", "\uE8A5", IsEnabled: false, ToolTip: "P4 阶段实现"));
        Actions.Add(new ActionItem("刷新", "F5", "\uE72C", IsEnabled: false, ToolTip: "P2e 阶段实现"));
        Actions.Add(new ActionItem("注释", "F6", "\uE70B", IsEnabled: false, ToolTip: "P4 阶段实现"));

        State = PageState.Empty;
        StateDetail = "打开本地文件或目标进程内存开始编辑；默认只读显示，需手动开启编辑模式。";
    }

    public HexDocument? Document => _document;

    public VirtualHexRows? Rows
    {
        get => _rows;
        set => SetProperty(ref _rows, value);
    }

    public bool EditMode
    {
        get => _editMode;
        set
        {
            if (SetProperty(ref _editMode, value))
            {
                OnPropertyChanged(nameof(EditModeText));
                OnPropertyChanged(nameof(CanApplyEdit));
            }
        }
    }

    public string EditModeText => EditMode ? "编辑模式：覆盖" : "只读";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string SourceLabel
    {
        get => _sourceLabel;
        set => SetProperty(ref _sourceLabel, value);
    }

    public string Encoding
    {
        get => _encoding;
        set => SetProperty(ref _encoding, value);
    }

    /// <summary>支持编码列表（规格 §5.3）。</summary>
    public IReadOnlyList<string> Encodings => TextCodec.Supported;

    public string FindText
    {
        get => _findText;
        set => SetProperty(ref _findText, value);
    }

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (SetProperty(ref _editValue, value))
            {
                OnPropertyChanged(nameof(CanApplyEdit));
            }
        }
    }

    public HexRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                OnPropertyChanged(nameof(CanApplyEdit));
                if (value is not null)
                {
                    StatusText = $"偏移 {value.OffsetText}（行 {value.Offset / 16}）";
                }
            }
        }
    }

    public bool CanSave => _document is { IsDirty: true } && _editableSource is not null;

    public bool CanUndo => _document is { EditCount: > 0 };

    public bool CanApplyEdit => EditMode && SelectedRow is not null && !string.IsNullOrWhiteSpace(EditValue);

    public ICommand OpenFileCommand { get; }

    public ICommand OpenProcessCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand FindCommand { get; }

    public ICommand ApplyEditCommand { get; }

    public ICommand ToggleEditModeCommand { get; }

    public override string WorkspacePlaceholder => "M03 十六进制工作台（P2 已实现）。";

    /// <summary>请求视图滚动到选中行（View 订阅后 ScrollIntoView；虚拟化 DataGrid 不会自动滚动）。</summary>
    public event Action? ScrollRequested;

    // ---------- 打开 ----------

    private async Task OpenFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "打开文件到十六进制视图",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await OpenFileCoreAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            State = PageState.Failed;
            StateDetail = $"打开失败：{ex.Message}";
        }
    }

    internal async Task OpenFileCoreAsync(string path)
    {
        var source = await FileByteSource.OpenAsync(path, readOnly: false, CancellationToken.None);
        _openedFilePath = path;
        await AttachSourceAsync(source, $"文件：{System.IO.Path.GetFileName(path)}");
    }

    private async Task OpenProcessAsync()
    {
        var input = new ProcessOpenDialog();
        if (input.ShowDialog() != true)
        {
            return;
        }
        try
        {
            await OpenProcessAtCoreAsync(input.ProcessId, input.BaseAddress, input.ViewLength, input.BaseAddress);
        }
        catch (Exception ex)
        {
            State = PageState.Failed;
            StateDetail = $"打开失败：{ex.Message}";
        }
    }

    /// <summary>按 PID/基址/长度打开进程内存视图并滚动到焦点地址（扫描候选“在编辑器中打开”跳转；探针/测试可复用）。</summary>
    public async Task OpenProcessAtCoreAsync(int pid, ulong baseAddress, ulong viewLength, ulong focusOffset)
    {
        _openedPid = pid;
        _openedBaseAddress = baseAddress;
        _openedViewLength = viewLength;
        _openedFocusOffset = focusOffset;
        _hasOpenedProcess = true;
        _openedFilePath = null;

        var source = new ProcessMemoryByteSource(pid, $"pid:{pid}", baseAddress, viewLength);
        await AttachSourceAsync(source, $"进程内存：PID {pid} @0x{baseAddress:X16}");

        // 关键：文档偏移 = 绝对地址 - 视图基址（视图只有 viewLength 字节，绝对地址必然越界）
        if (focusOffset < baseAddress || focusOffset - baseAddress >= viewLength)
        {
            StatusText = $"焦点地址 0x{focusOffset:X16} 不在视图范围内（0x{baseAddress:X16}~0x{baseAddress + viewLength:X16}）。";
            return;
        }
        var docOffset = focusOffset - baseAddress;
        ScrollToOffset(docOffset);
        SelectByte(docOffset);
        StatusText = $"已定位并选中字节 0x{focusOffset:X16}（PID {pid}）；点“编辑模式”后输入十六进制并“应用覆盖”，即写回进程内存。";
    }

    private async Task AttachSourceAsync(IEditableByteSource source, string label)
    {
        _document?.Dispose();
        _editableSource?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        _editableSource = source;
        _document = new HexDocument(source);
        Rows = new VirtualHexRows(_document);
        SourceLabel = label;
        EditMode = false;
        StatusText = $"已打开：{label}；长度 {_document.Length} 字节（{(source.Capabilities.HasFlag(ByteSourceCapabilities.Insert) ? "可插入/删除" : "仅等长覆盖")}）";
        State = PageState.Ready;
        StateDetail = source.Capabilities.HasFlag(ByteSourceCapabilities.AtomicCommit)
            ? "文件已打开；开启编辑模式后可覆盖，保存为原子提交。"
            : "进程内存视图；仅支持等长覆盖写入。";
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanUndo));
    }

    // ---------- 编辑/撤销/保存 ----------

    private void ToggleEditMode()
    {
        if (_document is null) return;
        EditMode = !EditMode;
        StatusText = EditMode ? "编辑模式已开启（覆盖写）" : "只读模式";
    }

    /// <summary>应用覆盖编辑。起点 = 当前选中的字节；文件来源写入文档缓存手动保存，进程内存立即写回。</summary>
    private async Task ApplyEditAsync()
    {
        if (_document is null)
        {
            StatusText = "未打开来源。";
            return;
        }
        if (SelectedByteOffset is not ulong startOffset)
        {
            if (SelectedRow is null)
            {
                StatusText = "请先点选一个字节。";
                return;
            }
            startOffset = SelectedRow.Offset;
        }
        if (!EditMode)
        {
            StatusText = "请先点击“编辑模式”开启覆盖编辑（进程内存只读视图需先开启）。";
            return;
        }

        var valueText = EditValue.Replace(" ", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (valueText.Length == 0 || valueText.Length % 2 != 0)
        {
            StatusText = "请输入偶数位十六进制（如 DE AD BE EF；从选中字节起覆盖）";
            return;
        }

        byte[] bytes;
        try
        {
            bytes = new byte[valueText.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(valueText.Substring(i * 2, 2), 16);
            }

            if (startOffset + (ulong)bytes.Length > _document.Length)
            {
                StatusText = "编辑范围超出文档长度";
                return;
            }

            _document.Overwrite(startOffset, bytes);
            RefreshVisibleRows(startOffset);
        }
        catch (Exception ex)
        {
            StatusText = $"编辑失败：{ex.Message}";
            return;
        }

        // 进程内存：应用即写回目标进程（无需手动保存）
        if (_editableSource is not null && !_editableSource.Capabilities.HasFlag(ByteSourceCapabilities.AtomicCommit))
        {
            var result = await _editableSource.CommitAsync(
                _document.BuildChangeSet(),
                new CommitOptions(VerifyVersionToken: false, CreateBackup: false),
                CancellationToken.None);
            if (result.Success)
            {
                StatusText = $"已写入进程内存 {bytes.Length} 字节 @{AddressFormatting.ToHex16(startOffset)}。";
                State = PageState.Ready;
                // 重新加载视图：文档干净、显示写入后的当前值
                if (_hasOpenedProcess)
                {
                    await OpenProcessAtCoreAsync(_openedPid, _openedBaseAddress, _openedViewLength, _openedFocusOffset);
                }
            }
            else
            {
                StatusText = $"写入被拒绝：{result.Message}";
                // 写前比较失败（游戏值已变化）或进程不可达：刷新视图便于重试
                if (_hasOpenedProcess)
                {
                    await OpenProcessAtCoreAsync(_openedPid, _openedBaseAddress, _openedViewLength, _openedFocusOffset);
                    StatusText += " 已刷新视图（读取最新值），可重新编辑。";
                }
            }
            return;
        }

        // 文件来源：保持手动保存
        StatusText = $"已覆盖 {bytes.Length} 字节 @{AddressFormatting.ToHex16(startOffset)}；可撤销或保存。";
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanUndo));
    }

    private void Undo()
    {
        if (_document is null) return;
        var undone = _document.Undo();
        if (undone is not null)
        {
            RefreshVisibleRows(undone.Offset);
            StatusText = $"已撤销：{undone.GetType().Name} @{AddressFormatting.ToHex16(undone.Offset)}";
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanUndo));
        }
    }

    private async Task SaveAsync()
    {
        if (_document is null || _editableSource is null) return;

        try
        {
            var result = await _editableSource.CommitAsync(
                _document.BuildChangeSet(),
                new CommitOptions(VerifyVersionToken: true, CreateBackup: true),
                CancellationToken.None);
            if (result.Success)
            {
                if (_openedFilePath is not null)
                {
                    // File.Replace 后旧流与旧 VersionToken 都不能继续用于后续编辑。
                    await OpenFileCoreAsync(_openedFilePath);
                }
                StatusText = $"已保存（备份 {System.IO.Path.GetFileName(result.RecoveryPath)}）";
                StateDetail = "保存成功，已重新加载新版本文件。";
            }
            else
            {
                StatusText = $"保存失败：{result.Message}";
                State = PageState.Failed;
                StateDetail = $"保存被拒绝：{result.Message}；可重新加载或另存。";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"保存异常：{ex.Message}";
        }
    }

    private async Task FindAsync()
    {
        if (_document is null || string.IsNullOrWhiteSpace(FindText)) return;

        try
        {
            var engine = new SearchEngine();
            var start = SelectedRow?.Offset ?? 0UL;
            SearchHit? hit;
            if (FindText.All(c => Uri.IsHexDigit(c) || char.IsWhiteSpace(c) || c is '?' or '*'))
            {
                hit = engine.FindHex(_document, SearchEngine.ParseHexPattern(FindText), start);
            }
            else
            {
                hit = engine.FindText(_document, FindText, Encoding, matchCase: false, start);
            }

            if (hit is null)
            {
                StatusText = "未找到匹配";
            }
            else
            {
                ScrollToOffset(hit.Offset);
                StatusText = $"找到 @{AddressFormatting.ToHex16(hit.Offset)}（{hit.Preview}）";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"查找失败：{ex.Message}";
        }
    }

    private void RefreshVisibleRows(ulong offset)
    {
        // 虚拟行集合：重建后 DataGrid 按需重新请求可见行（保持选择位置）
        if (_document is null) return;
        var keepOffset = SelectedRow?.Offset ?? offset;
        Rows = new VirtualHexRows(_document);
        ScrollToOffset(keepOffset);
    }

    /// <summary>滚动到指定偏移（选择对应行并通知视图滚动到可见区域）。</summary>
    public void ScrollToOffset(ulong offset)
    {
        _pendingFocusOffset = offset;
        FocusByteColumn = (int)(offset % 16);
        var rowIndex = (int)(offset / 16);
        if (Rows is not null && rowIndex < Rows.Count)
        {
            SelectedRow = Rows[rowIndex];
            ScrollRequested?.Invoke();
        }
    }

    /// <summary>页面就绪后消费缓存的定位请求（解决模块切换时滚动请求早于页面创建而丢失）。</summary>
    public void ConsumePendingFocus()
    {
        if (_pendingFocusOffset is ulong offset)
        {
            _pendingFocusOffset = null;
            ScrollToOffset(offset);
        }
    }

    /// <summary>字节级选中：定位到指定字节偏移并预填编辑值（View 单元格点击/定位时调用）。</summary>
    public void SelectByte(ulong byteOffset)
    {
        SelectedByteOffset = byteOffset;
        FocusByteColumn = (int)(byteOffset % 16);
        var rowIndex = (int)(byteOffset / 16);
        if (Rows is not null && rowIndex < Rows.Count)
        {
            SelectedRow = Rows[rowIndex];
        }
        if (_document is not null)
        {
            try
            {
                var b = _document.ReadByte(byteOffset);
                EditValue = b.ToString("X2");
            }
            catch
            {
                EditValue = "";
            }
        }
        StatusText = $"选中字节 0x{byteOffset:X}（行内第 {FocusByteColumn} 列）；输入新值后点“应用覆盖”即写回。";
    }

    public void NotifyDirtyChanged() => OnPropertyChanged(nameof(CanSave));
}
