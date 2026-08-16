using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>地址表条目（规格 §8：逻辑地址优先、宿主证据、标签/注释、立即写入、条件冻结）。</summary>
public sealed record AddressBookEntry(
    Guid Id,
    LogicalAddress Address,
    string Label,
    string Group,
    string Game,
    string? Author,
    string? Comment,
    ScanDataType DataType,
    Endianness Endianness,
    string? DisplayFormat,
    bool Enabled,
    string? CurrentValueText,
    string? WriteValueText);

/// <summary>冻结条件（原版锁定/条件）。</summary>
public enum FreezeConditionKind
{
    Always,
    Equals,
    NotEquals,
    InRange,
    Changed,
}

/// <summary>冻结请求：单调度器、最短 16ms、默认 250ms、连续失败停用、500ms 全停（规格 §9）。</summary>
public sealed record FreezeSpec(
    Guid EntryId,
    FreezeConditionKind Kind,
    ulong? Value,
    ulong? RangeStart,
    ulong? RangeEnd,
    TimeSpan Interval = default,
    int MaxConsecutiveFailures = 5);

public sealed record FreezeStatus(
    Guid EntryId,
    bool Running,
    long WriteCount,
    long FailureCount,
    string? LastError,
    TimeSpan Interval);

/// <summary>地址表服务（规格 §8）。</summary>
public interface IAddressBookService
{
    ValueTask<IReadOnlyList<AddressBookEntry>> ListAsync(CancellationToken ct);

    ValueTask<AddressBookEntry> UpsertAsync(AddressBookEntry entry, CancellationToken ct);

    ValueTask<bool> RemoveAsync(Guid entryId, CancellationToken ct);

    ValueTask ClearAsync(CancellationToken ct);

    /// <summary>立即写入一次（写前比较 + 读回校验由写入层负责）。</summary>
    ValueTask<bool> WriteNowAsync(Guid entryId, string? valueText, CancellationToken ct);

    /// <summary>刷新条目当前值。</summary>
    ValueTask RefreshValuesAsync(Guid entryId, CancellationToken ct);
}

/// <summary>冻结服务（规格 §8：优先队列、条件、间隔、失败熔断、全局停止）。</summary>
public interface IFreezeService
{
    ValueTask<FreezeStatus> StartAsync(FreezeSpec spec, CancellationToken ct);

    ValueTask<FreezeStatus> StopAsync(Guid entryId, CancellationToken ct);

    ValueTask StopAllAsync(CancellationToken ct);

    ValueTask<IReadOnlyList<FreezeStatus>> ListAsync(CancellationToken ct);
}
