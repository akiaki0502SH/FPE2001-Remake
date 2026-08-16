using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Contracts;

/// <summary>
/// 统一字节源（规格 §4）。
/// 64 位寻址；允许部分读取；任何 UI 结果携带 generationId 以丢弃过期响应。
/// </summary>
public interface IByteSource : IAsyncDisposable
{
    ByteSourceIdentity Identity { get; }

    ByteSourceCapabilities Capabilities { get; }

    /// <summary>
    /// 从 offset 开始尽量读满 destination；返回部分读取结果与不可读区间。
    /// </summary>
    ValueTask<ByteReadResult> ReadAsync(ulong offset, Memory<byte> destination, CancellationToken ct);
}

/// <summary>可编辑字节源：进程内存只支持等长覆盖；文件支持插入/删除并原子提交（规格 §4/§6）。</summary>
public interface IEditableByteSource : IByteSource
{
    ValueTask<CommitResult> CommitAsync(ChangeSet changes, CommitOptions options, CancellationToken ct);
}
