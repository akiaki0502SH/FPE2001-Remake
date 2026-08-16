using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>
/// 部分读取结果（规格 §4）。
/// CompletedLength：实际读入 destination 的字节数；
/// UnavailableRanges：本次请求中不可读的区间（如保护页/不可读内存）；
/// IsRetryableError：true 表示可重试的瞬时失败（如共享冲突）。
/// </summary>
public sealed record ByteReadResult(
    int CompletedLength,
    IReadOnlyList<ByteRange> UnavailableRanges,
    bool IsRetryableError)
{
    public static ByteReadResult Full(int length) => new(length, [], false);
}
