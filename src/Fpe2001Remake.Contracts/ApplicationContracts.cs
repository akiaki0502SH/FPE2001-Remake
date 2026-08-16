using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>应用层服务契约（UIUX 规格 §15 MVVM 映射）。实现位于 Application 程序集。</summary>

/// <summary>扫描应用服务（M01）。</summary>
public interface IScanApplicationService
{
    ValueTask<IReadOnlyList<(int ProcessId, string ProcessName)>> ListTargetsAsync(CancellationToken ct);

    ValueTask<ScanSession?> GetActiveSessionAsync(CancellationToken ct);
}

/// <summary>Mission 服务（原版任务概念；Mission 是用户命名工作，历史是不可变快照）。</summary>
public interface IMissionService
{
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken ct);

    ValueTask<string> CreateAsync(string name, CancellationToken ct);

    ValueTask RenameAsync(string oldName, string newName, CancellationToken ct);

    ValueTask<bool> DeleteAsync(string name, CancellationToken ct);
}

/// <summary>字节源工厂（M03；从文件/进程/快照创建 IByteSource）。</summary>
public interface IByteSourceFactory
{
    ValueTask<IByteSource> OpenFileAsync(string path, bool readOnly, CancellationToken ct);

    ValueTask<IByteSource> OpenProcessAsync(
        int processId, string domainId, ulong baseAddress, CancellationToken ct);

    ValueTask<IByteSource> OpenSnapshotAsync(string snapshotPath, CancellationToken ct);
}

/// <summary>十六进制编辑器服务（M03）。</summary>
public interface IHexEditorService
{
    ValueTask<IReadOnlyList<Guid>> ListSessionsAsync(CancellationToken ct);

    ValueTask CloseSessionAsync(Guid sessionId, bool saveChanges, CancellationToken ct);
}

/// <summary>文件目录目录服务（M04）。</summary>
public interface IFileCatalog
{
    IAsyncEnumerable<string> EnumerateDirectoriesAsync(string root, CancellationToken ct);
}

/// <summary>速度控制服务（M07，聚合目标/适配器的 ISpeedControlCapability）。</summary>
public interface ISpeedControlService
{
    ValueTask<SpeedState?> GetStateAsync(CancellationToken ct);

    ValueTask<SpeedState> SetEnabledAsync(bool enabled, CancellationToken ct);

    ValueTask<SpeedState> SetMultiplierAsync(double multiplier, CancellationToken ct);
}

/// <summary>设置服务（M08）。</summary>
public interface ISettingsService
{
    ValueTask<T> GetAsync<T>(string key, T fallback, CancellationToken ct);

    ValueTask SetAsync<T>(string key, T value, CancellationToken ct);
}

/// <summary>旧配置导入预览（M08 / P3）。</summary>
public interface ILegacyImportService
{
    ValueTask<LegacyImportPreview> PreviewAsync(string path, CancellationToken ct);
}

public sealed record LegacyImportPreview(
    string Path,
    string Format,
    IReadOnlyList<LegacyImportField> Fields,
    IReadOnlyList<string> Warnings);

public sealed record LegacyImportField(string Name, string RawValue, bool IsUnknown);

/// <summary>版本服务（M09）。</summary>
public interface IVersionService
{
    string ProductVersion { get; }

    string ProtocolVersion { get; }
}

/// <summary>诊断服务（M09；诊断默认不包含完整内存、文件内容或完整个人路径）。</summary>
public interface IDiagnosticsService
{
    Task<string> CollectSystemInfoAsync(CancellationToken ct);

    Task ExportDiagnosticsAsync(string targetPath, CancellationToken ct);
}
