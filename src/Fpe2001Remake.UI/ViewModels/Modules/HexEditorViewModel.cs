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

    public HexRow(HexDocument document, ulong offset)
    {
        _document = document;
        Offset = offset;
    }

    public ulong Offset { get; }

    public string OffsetText => AddressFormatting.ToHex16(Offset);

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
        Span<char> hexChars = stackalloc char[read * 3];
        for (var i = 0; i < read; i++)
        {
            var available = _document.IsRangeAvailable(Offset + (ulong)i, 1);
            hexChars[i * 3] = available ? HexChars[data[i] >> 4] : '?';
            hexChars[i * 3 + 1] = available ? HexChars[data[i] & 0x0F] : '?';
            hexChars[i * 3 + 2] = ' ';
        }
        Hex = new string(hexChars).TrimEnd();

        Span<char> textChars = stackalloc char[read];
        for (var i = 0; i < read; i++)
        {
            textChars[i] = _document.IsRangeAvailable(Offset + (ulong)i, 1) && data[i] is >= 0x20 and <= 0x7E
                ? (char)data[i] : '.';
        }
        Text = new string(textChars);
        IsModified = _document.IsDirtyInRange(Offset, read);
    }

    private static readonly char[] HexChars = "0123456789ABCDEF".ToCharArray();
}

/// <summary>虚拟行集合：O(1) 索引，DataGrid 虚拟化按需取行。</summary>
public sealed class VirtualHexRows : IList<HexRow>
{
    private readonly HexDocument _document;
    private readonly ulong _rowCount;

    public VirtualHexRows(HexDocument document)
    {
        _document = document;
        _rowCount = (document.Length + 15) / 16;
    }

    public HexRow this[int index]
    {
        get
        {
            var row = new HexRow(_document, (ulong)index * 16);
            row.Refresh();
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
        ApplyEditCommand = new RelayCommand(_ => ApplyEdit(), _ => CanApplyEdit);
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
        set => SetProperty(ref _editValue, value);
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
            var source = new ProcessMemoryByteSource(input.ProcessId, $"pid:{input.ProcessId}", input.BaseAddress, input.ViewLength);
            _openedFilePath = null;
            await AttachSourceAsync(source, $"进程内存：PID {input.ProcessId} @0x{input.BaseAddress:X16}");
        }
        catch (Exception ex)
        {
            State = PageState.Failed;
            StateDetail = $"打开失败：{ex.Message}";
        }
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

    private void ApplyEdit()
    {
        if (_document is null || SelectedRow is null || !EditMode) return;

        var rowOffset = SelectedRow.Offset;
        var valueText = EditValue.Replace(" ", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (valueText.Length == 0 || valueText.Length % 2 != 0)
        {
            StatusText = "请输入偶数位十六进制（如 DE AD BE EF）";
            return;
        }

        try
        {
            var bytes = new byte[valueText.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(valueText.Substring(i * 2, 2), 16);
            }

            if (rowOffset + (ulong)bytes.Length > _document.Length)
            {
                StatusText = "编辑范围超出文档长度";
                return;
            }

            _document.Overwrite(rowOffset, bytes);
            RefreshVisibleRows(rowOffset);
            StatusText = $"已覆盖 {bytes.Length} 字节 @{AddressFormatting.ToHex16(rowOffset)}；可撤销或保存。";
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanUndo));
        }
        catch (Exception ex)
        {
            StatusText = $"编辑失败：{ex.Message}";
        }
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

    /// <summary>滚动到指定偏移（选择对应行）。</summary>
    public void ScrollToOffset(ulong offset)
    {
        var rowIndex = (int)(offset / 16);
        if (Rows is not null && rowIndex < Rows.Count)
        {
            SelectedRow = Rows[rowIndex];
        }
    }

    public void NotifyDirtyChanged() => OnPropertyChanged(nameof(CanSave));
}
