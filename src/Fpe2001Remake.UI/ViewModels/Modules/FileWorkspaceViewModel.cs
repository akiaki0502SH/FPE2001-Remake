using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>目录树节点（惰性加载子目录）。</summary>
public sealed class DirectoryNode : ViewModelBase
{
    private bool _isExpanded;
    private bool _isLoaded;

    public DirectoryNode(string path, string name)
    {
        Path = path;
        Name = name;
        Children = new ObservableCollection<DirectoryNode>();
    }

    public string Path { get; }

    public string Name { get; }

    public ObservableCollection<DirectoryNode> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
            {
                LoadChildren();
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>惰性加载子目录（只读一次）。</summary>
    public void LoadChildren()
    {
        if (IsLoaded) return;
        IsLoaded = true;
        try
        {
            var dirs = Directory.EnumerateDirectories(Path)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var d in dirs)
            {
                try
                {
                    Children.Add(new DirectoryNode(d, System.IO.Path.GetFileName(d)));
                }
                catch
                {
                    // 跳过不可访问目录
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录不可访问：空节点
        }
    }
}

/// <summary>文件列表行。</summary>
public sealed class FileRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required long Size { get; init; }

    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024.0 / 1024.0:0.0} MiB"
        : Size >= 1024 ? $"{Size / 1024.0:0.0} KiB" : $"{Size} B";

    public required DateTime Modified { get; init; }

    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>扫描命中行。</summary>
public sealed class ScanHitRow
{
    public required string Path { get; init; }

    public required ulong Offset { get; init; }

    public string OffsetText => Domain.AddressFormatting.ToHex16(Offset);

    public required int Length { get; init; }

    public required string Preview { get; init; }
}

/// <summary>
/// M04 文件工作区（规格 §7）：目录浏览 + 文件列表 + 内容扫描（Hex/Text/Integer/Float）→ 命中打开到十六进制。
/// </summary>
public sealed class FileWorkspaceViewModel : ModuleViewModelBase
{
    private readonly IFileScanService _scanService;
    private readonly Action<string> _openInEditor;
    private readonly CancellationTokenSource _lifetime = new();

    private DirectoryNode? _root;
    private DirectoryNode? _selectedDirectory;
    private IReadOnlyList<FileRow> _files = [];
    private string _pattern = "";
    private string _scanType = "Hex";
    private bool _recursive = true;
    private bool _isScanning;
    private string _scanStatus = "";
    private FileRow? _selectedFile;
    private ScanHitRow? _selectedHit;

    public FileWorkspaceViewModel(IFileScanService scanService, Action<string> openInEditor)
        : base(ModuleCatalog.Files)
    {
        _scanService = scanService;
        _openInEditor = openInEditor;

        RefreshRootCommand = new RelayCommand(_ => LoadRoot());
        ScanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => !IsScanning);
        StopScanCommand = new RelayCommand(_ => _scanCts?.Cancel(), _ => IsScanning);
        OpenInEditorCommand = new RelayCommand(_ => OpenSelectedFile(), _ => SelectedFile is not null || SelectedHit is not null);

        Actions.Add(new ActionItem("刷新", "F1", "\uE72C", IsEnabled: true, Command: RefreshRootCommand));
        Actions.Add(new ActionItem("扫描内容", "F2", "\uE721", IsEnabled: true, Command: ScanCommand));
        Actions.Add(new ActionItem("停止", "F3", "\uE71A", Command: StopScanCommand));
        Actions.Add(new ActionItem("打开", "F4", "\uE8E5", Command: OpenInEditorCommand, ToolTip: "在十六进制编辑器中打开"));
        Actions.Add(new ActionItem("向上", "F5", "\uE74A", IsEnabled: false, ToolTip: "P3 阶段实现"));

        State = PageState.Ready;
        StateDetail = "选择目录浏览文件；输入十六进制/文本模式扫描文件内容。";

        // 自动加载第一个驱动器作为根目录
        LoadRoot();
    }

    public DirectoryNode? Root
    {
        get => _root;
        set => SetProperty(ref _root, value);
    }

    public DirectoryNode? SelectedDirectory
    {
        get => _selectedDirectory;
        set
        {
            if (SetProperty(ref _selectedDirectory, value) && value is not null)
            {
                LoadFiles(value.Path);
            }
        }
    }

    public IReadOnlyList<FileRow> Files
    {
        get => _files;
        set => SetProperty(ref _files, value);
    }

    public FileRow? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                OnPropertyChanged(nameof(OpenInEditorCommand));
            }
        }
    }

    public ScanHitRow? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (SetProperty(ref _selectedHit, value))
            {
                OnPropertyChanged(nameof(OpenInEditorCommand));
            }
        }
    }

    public string Pattern
    {
        get => _pattern;
        set => SetProperty(ref _pattern, value);
    }

    public string ScanType
    {
        get => _scanType;
        set => SetProperty(ref _scanType, value);
    }

    public IReadOnlyList<string> ScanTypes { get; } = ["Hex", "Text", "UInt16", "UInt32", "UInt64"];

    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanCommand));
                OnPropertyChanged(nameof(StopScanCommand));
            }
        }
    }

    public string ScanStatus
    {
        get => _scanStatus;
        set => SetProperty(ref _scanStatus, value);
    }

    public ObservableCollection<ScanHitRow> Hits { get; } = [];

    public ICommand RefreshRootCommand { get; }

    public ICommand ScanCommand { get; }

    public ICommand StopScanCommand { get; }

    public ICommand OpenInEditorCommand { get; }

    public override string WorkspacePlaceholder => "M04 文件工作台（P3 已实现）。";

    private CancellationTokenSource? _scanCts;

    public void LoadRoot()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
        var root = new DirectoryNode(drives[0], drives[0]);
        root.LoadChildren();
        Root = root;
        SelectedDirectory = root;
    }

    private void LoadFiles(string dirPath)
    {
        try
        {
            var rows = Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly)
                .Select(f =>
                {
                    try
                    {
                        var info = new FileInfo(f);
                        return new FileRow
                        {
                            Path = f,
                            Name = info.Name,
                            Size = info.Length,
                            Modified = info.LastWriteTime,
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(r => r is not null)
                .Cast<FileRow>()
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Files = rows;
            ScanStatus = $"{rows.Length} 个文件";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Files = [];
            ScanStatus = $"无法读取目录：{ex.Message}";
        }
    }

    private async Task ScanAsync()
    {
        if (IsScanning || SelectedDirectory is null || string.IsNullOrWhiteSpace(Pattern))
        {
            return;
        }

        try
        {
            IsScanning = true;
            Hits.Clear();
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

            var predicate = BuildPredicate();
            var request = new FileScanRequest(
                SelectedDirectory.Path, Recursive, FollowReparsePoints: false,
                Filter: null, Predicate: predicate,
                MaxConcurrency: 4, MaxBytes: null, MaxMatchesPerFile: 50);

            var sw = Stopwatch.StartNew();
            var progress = new Progress<FileScanProgress>(p =>
                ScanStatus = $"已访问 {p.FilesVisited} 文件 · {p.BytesRead / 1024.0 / 1024.0:0.0} MiB · 命中 {p.Matches} · 跳过 {p.Skipped} · 错误 {p.Errors} · {p.Elapsed.TotalSeconds:0.0}s");

            await foreach (var match in _scanService.ScanAsync(request, progress, _scanCts.Token))
            {
                Hits.Add(new ScanHitRow
                {
                    Path = match.Path,
                    Offset = match.Offset,
                    Length = match.Length,
                    Preview = match.Preview,
                });
                if (Hits.Count >= 5000)
                {
                    _scanCts.Cancel();
                    ScanStatus = "命中数达到 5000 上限，已停止。";
                    break;
                }
            }

            ScanStatus = $"扫描完成：{Hits.Count} 处命中（{sw.Elapsed.TotalSeconds:0.0}s）";
            State = Hits.Count > 0 ? PageState.Ready : PageState.Empty;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = $"已停止：{Hits.Count} 处命中";
        }
        catch (Exception ex)
        {
            ScanStatus = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private ContentPredicate BuildPredicate()
    {
        return ScanType switch
        {
            "Text" => new ContentPredicate(ScanDataType.Text, Pattern, Encoding: "UTF-8", MatchCase: false),
            "UInt16" => new ContentPredicate(ScanDataType.UInt16, Pattern, Endianness: Domain.Endianness.LittleEndian),
            "UInt32" => new ContentPredicate(ScanDataType.UInt32, Pattern, Endianness: Domain.Endianness.LittleEndian),
            "UInt64" => new ContentPredicate(ScanDataType.UInt64, Pattern, Endianness: Domain.Endianness.LittleEndian),
            _ => new ContentPredicate(ScanDataType.Bytes, Pattern, IsWildcardHex: true),
        };
    }

    private void OpenSelectedFile()
    {
        var hit = SelectedHit;
        var file = hit is not null ? hit.Path : SelectedFile?.Path;
        if (file is null) return;
        _openInEditor(file);
    }

    public void Shutdown()
    {
        _scanCts?.Cancel();
        _lifetime.Cancel();
    }
}
