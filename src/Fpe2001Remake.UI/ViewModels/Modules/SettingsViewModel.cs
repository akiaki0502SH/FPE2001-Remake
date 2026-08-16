using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>导入预览行。</summary>
public sealed class ImportFieldRow
{
    public required string Name { get; init; }

    public required string Value { get; init; }

    public required bool IsUnknown { get; init; }
}

/// <summary>
/// M08 设置（规格 §11）：扫描 / 文件与编辑 / 写入与锁定 / 兼容与隐私 分组；
/// 保存到 JsonSettingsService；旧配置导入预览（P3）。
/// </summary>
public sealed class SettingsViewModel : ModuleViewModelBase
{
    private readonly ISettingsService? _settings;
    private readonly ILegacyImportService? _legacy;

    private string _memoryLimitMb = "1024";
    private string _tempDirectory = "";
    private bool _backupEnabled = true;
    private string _undoQuotaMb = "128";
    private string _freezeIntervalMs = "250";
    private string _scanBlockSizeKb = "256";
    private string _importPath = "";
    private string _importStatus = "";
    private string _statusText = "";

    public SettingsViewModel(ISettingsService? settings = null, ILegacyImportService? legacy = null)
        : base(ModuleCatalog.Settings)
    {
        _settings = settings;
        _legacy = legacy;

        SaveCommand = new RelayCommand(_ => _ = SaveAsync());
        ResetCommand = new RelayCommand(_ => ResetDefaults());
        ImportPreviewCommand = new RelayCommand(_ => _ = ImportPreviewAsync(), _ => !string.IsNullOrWhiteSpace(ImportPath));

        Actions.Add(new ActionItem("保存", null, "\uE74E", IsEnabled: true, Command: SaveCommand));
        Actions.Add(new ActionItem("恢复默认", null, "\uE72C", IsEnabled: true, Command: ResetCommand));

        State = PageState.Ready;
        StateDetail = "分组：扫描 / 文件与编辑 / 写入与锁定 / 兼容与隐私。";

        _ = LoadAsync();
    }

    // ---- 扫描 ----
    public string MemoryLimitMb { get => _memoryLimitMb; set => SetProperty(ref _memoryLimitMb, value); }

    public string ScanBlockSizeKb { get => _scanBlockSizeKb; set => SetProperty(ref _scanBlockSizeKb, value); }

    // ---- 文件与编辑 ----
    public string TempDirectory { get => _tempDirectory; set => SetProperty(ref _tempDirectory, value); }

    public bool BackupEnabled { get => _backupEnabled; set => SetProperty(ref _backupEnabled, value); }

    public string UndoQuotaMb { get => _undoQuotaMb; set => SetProperty(ref _undoQuotaMb, value); }

    // ---- 写入与锁定 ----
    public string FreezeIntervalMs { get => _freezeIntervalMs; set => SetProperty(ref _freezeIntervalMs, value); }

    // ---- 兼容与隐私 ----
    public string ImportPath { get => _importPath; set { if (SetProperty(ref _importPath, value)) OnPropertyChanged(nameof(ImportPreviewCommand)); } }

    public string ImportStatus { get => _importStatus; set => SetProperty(ref _importStatus, value); }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ObservableCollection<ImportFieldRow> ImportFields { get; } = [];

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand ImportPreviewCommand { get; }

    public override string WorkspacePlaceholder => "M08 设置（P4 已实现）。";

    private async Task LoadAsync()
    {
        if (_settings is null) return;
        MemoryLimitMb = (await _settings.GetAsync("scan.memoryLimitMb", "1024", CancellationToken.None)).ToString();
        ScanBlockSizeKb = (await _settings.GetAsync("scan.blockSizeKb", "256", CancellationToken.None)).ToString();
        TempDirectory = await _settings.GetAsync("files.tempDirectory", "", CancellationToken.None);
        BackupEnabled = await _settings.GetAsync("files.backupEnabled", true, CancellationToken.None);
        UndoQuotaMb = (await _settings.GetAsync("files.undoQuotaMb", "128", CancellationToken.None)).ToString();
        FreezeIntervalMs = (await _settings.GetAsync("lock.freezeIntervalMs", "250", CancellationToken.None)).ToString();
    }

    private async Task SaveAsync()
    {
        if (_settings is null)
        {
            StatusText = "设置服务不可用。";
            return;
        }
        try
        {
            await _settings.SetAsync("scan.memoryLimitMb", int.Parse(MemoryLimitMb), CancellationToken.None);
            await _settings.SetAsync("scan.blockSizeKb", int.Parse(ScanBlockSizeKb), CancellationToken.None);
            await _settings.SetAsync("files.tempDirectory", TempDirectory, CancellationToken.None);
            await _settings.SetAsync("files.backupEnabled", BackupEnabled, CancellationToken.None);
            await _settings.SetAsync("files.undoQuotaMb", int.Parse(UndoQuotaMb), CancellationToken.None);
            await _settings.SetAsync("lock.freezeIntervalMs", int.Parse(FreezeIntervalMs), CancellationToken.None);
            StatusText = "设置已保存";
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
    }

    private void ResetDefaults()
    {
        MemoryLimitMb = "1024";
        ScanBlockSizeKb = "256";
        TempDirectory = "";
        BackupEnabled = true;
        UndoQuotaMb = "128";
        FreezeIntervalMs = "250";
        StatusText = "已恢复默认值（保存后生效）";
    }

    private async Task ImportPreviewAsync()
    {
        if (_legacy is null)
        {
            ImportStatus = "导入服务不可用。";
            return;
        }
        ImportFields.Clear();
        try
        {
            var preview = await _legacy.PreviewAsync(ImportPath, CancellationToken.None);
            foreach (var f in preview.Fields)
            {
                ImportFields.Add(new ImportFieldRow { Name = f.Name, Value = f.RawValue, IsUnknown = f.IsUnknown });
            }
            ImportStatus = $"格式：{preview.Format}；字段 {preview.Fields.Count} 个；警告 {preview.Warnings.Count} 条";
            if (preview.Warnings.Count > 0)
            {
                ImportStatus += "（" + string.Join("；", preview.Warnings.Take(3)) + "）";
            }
        }
        catch (Exception ex)
        {
            ImportStatus = $"预览失败：{ex.Message}";
        }
    }
}
