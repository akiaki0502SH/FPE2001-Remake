using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>媒体条目行（含缩略图）。</summary>
public sealed class MediaItemRow
{
    public required MediaItem Item { get; init; }

    public required string Name { get; init; }

    public required string Format { get; init; }

    public required string SizeText { get; init; }

    public required string DimensionText { get; init; }

    public BitmapImage? Thumbnail { get; set; }
}

/// <summary>M05 图片工作区（规格 §9）：文件夹浏览 + 缩略图 + 导出/重命名/删除（回收站）。</summary>
public sealed class MediaWorkspaceViewModel : ModuleViewModelBase
{
    private readonly IMediaWorkspace _workspace;
    private string _folderPath = "";
    private string _statusText = "";
    private MediaItemRow? _selected;

    public MediaWorkspaceViewModel(IMediaWorkspace? workspace = null)
        : base(ModuleCatalog.Picture)
    {
        _workspace = workspace ?? new Fpe2001Remake.Media.MediaWorkspace();

        BrowseCommand = new RelayCommand(_ => _ = BrowseAsync());
        LoadCommand = new RelayCommand(_ => _ = LoadAsync(), _ => !string.IsNullOrWhiteSpace(FolderPath));
        ExportPngCommand = new RelayCommand(_ => _ = ExportAsync("png"), _ => Selected is not null);
        ExportJpgCommand = new RelayCommand(_ => _ = ExportAsync("jpg"), _ => Selected is not null);
        DeleteCommand = new RelayCommand(_ => _ = DeleteAsync(), _ => Selected is not null);
        RenameCommand = new RelayCommand(_ => _ = RenameAsync(), _ => Selected is not null);

        Actions.Add(new ActionItem("浏览", "F1", "\uE8B7", IsEnabled: true, Command: BrowseCommand));
        Actions.Add(new ActionItem("加载", "F2", "\uE72C", IsEnabled: true, Command: LoadCommand));
        Actions.Add(new ActionItem("导出 PNG", null, "\uE74E", Command: ExportPngCommand));
        Actions.Add(new ActionItem("导出 JPG", null, "\uE74E", Command: ExportJpgCommand));
        Actions.Add(new ActionItem("删除", "F3", "\uE74D", Command: DeleteCommand, ToolTip: "移动到回收站"));
        Actions.Add(new ActionItem("重命名", "F4", "\uE8AC", Command: RenameCommand));

        State = PageState.Ready;
        StateDetail = "输入文件夹路径或浏览选择；列表为图片文件（PNG/JPG/BMP/GIF/WebP）。";
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                OnPropertyChanged(nameof(LoadCommand));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ObservableCollection<MediaItemRow> Items { get; } = [];

    public MediaItemRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(ExportPngCommand));
                OnPropertyChanged(nameof(ExportJpgCommand));
                OnPropertyChanged(nameof(DeleteCommand));
                OnPropertyChanged(nameof(RenameCommand));
            }
        }
    }

    public ICommand BrowseCommand { get; }

    public ICommand LoadCommand { get; }

    public ICommand ExportPngCommand { get; }

    public ICommand ExportJpgCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand RenameCommand { get; }

    /// <summary>点击缩略图选中。</summary>
    public ICommand SelectImageCommand { get; } = new RelayCommand(obj =>
    {
        if (obj is MediaItemRow row)
        {
            // 由页面 code-behind 处理选中（ItemsControl 无 SelectedItem）
        }
    });

    public override string WorkspacePlaceholder => "M05 图片工作台（P4 已实现）。";

    private async Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择图片文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            FolderPath = dialog.FolderName;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusText = "文件夹不存在。";
            return;
        }

        Items.Clear();
        StatusText = "加载中…";
        var count = 0;
        try
        {
            await foreach (var item in _workspace.EnumerateAsync(FolderPath, CancellationToken.None))
            {
                var row = new MediaItemRow
                {
                    Item = item,
                    Name = item.Name,
                    Format = item.Format,
                    SizeText = item.SizeBytes >= 1024 * 1024
                        ? $"{item.SizeBytes / 1024.0 / 1024.0:0.0} MiB"
                        : $"{item.SizeBytes / 1024.0:0.0} KiB",
                    DimensionText = $"{item.Width}×{item.Height}",
                };
                row.Thumbnail = LoadThumbnail(item.Identity.Path);
                Items.Add(row);
                count++;
            }
            StatusText = $"共 {count} 张图片";
            State = count > 0 ? PageState.Ready : PageState.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败：{ex.Message}";
        }
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private async Task ExportAsync(string format)
    {
        if (Selected is null) return;
        try
        {
            var target = Path.Combine(Path.GetDirectoryName(Selected.Item.Identity.Path)!,
                $"{Path.GetFileNameWithoutExtension(Selected.Item.Identity.Path)}.{format}");
            await _workspace.ExportAsync(Selected.Item.Identity, new MediaExportOptions(format, JpegQuality: 90, TargetPath: target), CancellationToken.None);
            StatusText = $"已导出：{Path.GetFileName(target)}";
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败：{ex.Message}";
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        try
        {
            await _workspace.DeleteAsync(Selected.Item.Identity, DeletePolicy.RecycleBin, CancellationToken.None);
            Items.Remove(Selected);
            Selected = null;
            StatusText = "已移动到回收站";
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
        }
    }

    private async Task RenameAsync()
    {
        if (Selected is null) return;
        var input = new Views.TextInputDialog("重命名图片", "新名称", Selected.Name);
        if (input.ShowDialog() != true)
        {
            return;
        }
        try
        {
            var old = Selected;
            var updated = await _workspace.RenameAsync(old.Item.Identity, input.ValueText, CancellationToken.None);
            var idx = Items.IndexOf(old);
            var row = new MediaItemRow
            {
                Item = updated,
                Name = updated.Name,
                Format = updated.Format,
                SizeText = $"{updated.SizeBytes / 1024.0:0.0} KiB",
                DimensionText = $"{updated.Width}×{updated.Height}",
                Thumbnail = old.Thumbnail,
            };
            if (idx >= 0)
            {
                Items[idx] = row;
            }
            Selected = row;
            StatusText = $"已重命名为 {updated.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"重命名失败：{ex.Message}";
        }
    }
}
