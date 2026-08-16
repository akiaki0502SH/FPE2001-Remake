namespace Fpe2001Remake.Contracts;

/// <summary>媒体身份（路径 + 版本标记）。</summary>
public sealed record MediaIdentity(string Path, string VersionToken);

/// <summary>媒体条目。</summary>
public sealed record MediaItem(
    MediaIdentity Identity,
    string Name,
    string Format,
    long Width,
    long Height,
    long SizeBytes,
    DateTimeOffset ModifiedUtc);

/// <summary>导出选项（规格 §9：格式、JPG 质量、目标路径）。</summary>
public sealed record MediaExportOptions(
    string Format,
    int? JpegQuality = null,
    string? TargetPath = null,
    int? MaxWidth = null,
    int? MaxHeight = null);

public enum DeletePolicy
{
    RecycleBin,
    Permanent,
}

/// <summary>图片工作区（规格 §9）。解码在后台并限制像素数；删除优先回收站。</summary>
public interface IMediaWorkspace
{
    IAsyncEnumerable<MediaItem> EnumerateAsync(string folder, CancellationToken ct);

    Task<MediaItem> RenameAsync(MediaIdentity item, string newName, CancellationToken ct);

    Task ExportAsync(MediaIdentity item, MediaExportOptions options, CancellationToken ct);

    Task DeleteAsync(MediaIdentity item, DeletePolicy policy, CancellationToken ct);
}
