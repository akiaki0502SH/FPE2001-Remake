using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>文件过滤器（扩展名/大小/时间，规格 §8 M04）。</summary>
public sealed record FileFilter(
    string? ExtensionPattern = null,
    ulong? MinSize = null,
    ulong? MaxSize = null,
    DateTimeOffset? ModifiedAfter = null,
    DateTimeOffset? ModifiedBefore = null);

/// <summary>内容匹配谓词（Hex/Text/Integer/Float，规格 §5.3）。</summary>
public sealed record ContentPredicate(
    ScanDataType DataType,
    string Pattern,
    string? Encoding = null,
    Endianness Endianness = Endianness.LittleEndian,
    bool MatchCase = false,
    bool IsWildcardHex = false);

/// <summary>文件扫描请求（规格 §7）。默认不跟随 reparse point；路径使用长路径兼容 API。</summary>
public sealed record FileScanRequest(
    string RootPath,
    bool Recursive,
    bool FollowReparsePoints,
    FileFilter? Filter,
    ContentPredicate? Predicate,
    int MaxConcurrency,
    ulong? MaxBytes,
    int MaxMatchesPerFile);

/// <summary>文件匹配结果（规格 §7）。</summary>
public sealed record FileMatch(
    string Path,
    ulong Offset,
    int Length,
    string Preview,
    string VersionToken,
    string MatchKind);

/// <summary>扫描进度：已访问文件数、字节数、匹配数、跳过数和取消（规格 §8 M04）。</summary>
public sealed record FileScanProgress(
    long FilesVisited,
    ulong BytesRead,
    long Matches,
    long Skipped,
    long Errors,
    TimeSpan Elapsed);

/// <summary>
/// 文件目录与内容扫描服务（规格 §7）。
/// 目录枚举与内容读取分离；单个文件失败计入 SkipReason，不让整个任务失败。
/// </summary>
public interface IFileScanService
{
    IAsyncEnumerable<FileMatch> ScanAsync(
        FileScanRequest request,
        IProgress<FileScanProgress>? progress,
        CancellationToken ct);
}
