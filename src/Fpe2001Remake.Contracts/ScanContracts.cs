using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>扫描数据类型（原版 8/16/32/未知 + 现代化扩展）。</summary>
public enum ScanDataType
{
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float32,
    Float64,
    Bytes,
    Text,
    Unknown,

    /// <summary>自动：同时按 8/16/32 位整数扫描，再次扫描时各自过滤并收敛出真实类型。</summary>
    Auto,
}

/// <summary>扫描步骤：首次 / 再次（原版 Mission 语义）。</summary>
public enum ScanStepKind
{
    FirstScan,
    NextScan,
}

/// <summary>比较条件。</summary>
public enum ScanComparison
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    Changed,
    Unchanged,
    UnknownValue,
}

/// <summary>输入值规格（类型 + 端序 + 可选掩码/文本编码）。</summary>
public sealed record ScanValueSpec(
    ScanDataType DataType,
    string? ValueText,
    Endianness Endianness = Endianness.LittleEndian,
    string? Encoding = null,
    ulong? Alignment = null);

/// <summary>单次扫描条件（首次给定值或未知；再次给定比较）。</summary>
public sealed record ScanCondition(
    ScanStepKind Step,
    ScanComparison Comparison,
    ScanValueSpec? Value);

/// <summary>扫描进度（规格 §4 M01 内存摘要）。</summary>
public sealed record ScanProgress(
    ulong PlannedBytes,
    ulong ReadBytes,
    long RegionsVisited,
    long RegionsSkipped,
    long Candidates,
    int SnapshotCount,
    TimeSpan Elapsed)
{
    public double Percent => PlannedBytes == 0 ? 0 : Math.Min(100.0, ReadBytes * 100.0 / PlannedBytes);
}

/// <summary>扫描候选（规格 §4 M01 候选结果）。</summary>
public sealed record ScanCandidate(
    LogicalAddress Address,
    string? CurrentValueText,
    string? PreviousValueText,
    string? HostEvidence,
    ulong? HostAddress);

/// <summary>扫描会话：结果落磁盘快照，不驻留内存（规格 §9 结果）。</summary>
public sealed record ScanSession(
    Guid Id,
    string MissionName,
    int ProcessId,
    string ProcessName,
    ScanStepKind CurrentStep,
    long CandidateCount,
    string SnapshotPath,
    IReadOnlyList<ScanSnapshotInfo>? SnapshotStats = null);

/// <summary>单个快照的类型与候选数（自动模式 8/16/32 位各一份；单类型一份）。</summary>
public sealed record ScanSnapshotInfo(ScanDataType DataType, long Count);

/// <summary>
/// 扫描引擎（规格 §8）。首次/再次扫描、Mission、进度、取消、FPSN 快照。
/// 取消不是错误：保留上个完整快照。
/// </summary>
public interface IScanEngine
{
    ValueTask<ScanSession> BeginFirstScanAsync(
        int processId,
        string missionName,
        ScanCondition condition,
        LogicalAddress? rangeStart,
        LogicalAddress? rangeEnd,
        IProgress<ScanProgress>? progress,
        CancellationToken ct);

    ValueTask<ScanSession> RunNextScanAsync(
        Guid sessionId,
        ScanCondition condition,
        IProgress<ScanProgress>? progress,
        CancellationToken ct);

    ValueTask<ScanSession> CancelAsync(Guid sessionId, CancellationToken ct);

    ValueTask<IReadOnlyList<ScanCandidate>> ReadCandidatesAsync(
        Guid sessionId, int offset, int count, CancellationToken ct);
}

/// <summary>内存区域规划（规格 §8）：VirtualQueryEx、保护过滤、checked 推进。</summary>
public interface IMemoryRegionProvider
{
    /// <summary>枚举进程可读提交区域（跳过 NOACCESS/GUARD），返回逻辑地址区间。</summary>
    IAsyncEnumerable<ByteRange> EnumerateReadableRegionsAsync(
        int processId, ulong start, ulong end, CancellationToken ct);
}
