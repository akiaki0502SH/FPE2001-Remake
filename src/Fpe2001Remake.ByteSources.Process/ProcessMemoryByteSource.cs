using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Memory.Win32;

namespace Fpe2001Remake.ByteSources.Process;

/// <summary>
/// 进程内存字节源（规格 §6）：
/// 逻辑地址解析、区域读取（不可读显示 ?? → UnavailableRanges）、等长覆盖写入。
/// 禁止插入/删除；写入使用 ExpectedCurrentValue（Before）与读回校验；目标重启后所有提交拒绝。
/// </summary>
public sealed class ProcessMemoryByteSource : IEditableByteSource
{
    private const int ReadChunk = 64 * 1024;

    private readonly int _processId;
    private readonly string _domainId;
    private readonly ulong _baseAddress;
    private readonly ulong _viewLength;
    private readonly ByteSourceIdentity _identity;
    private readonly object _gate = new();
    private bool _disposed;

    public ProcessMemoryByteSource(int processId, string domainId, ulong baseAddress, ulong viewLength)
    {
        // 允许打开"已退出进程"的占位身份：提交时复验拒绝（规格：目标重启后所有提交拒绝）
        var pidIdentity = ProcessIdentity.FromId(processId);
        _processId = processId;
        _domainId = domainId;
        _baseAddress = baseAddress;
        _viewLength = viewLength;
        _identity = new ByteSourceIdentity(
            Guid.NewGuid(), ByteSourceKind.ProcessMemory,
            $"pid:{processId} {domainId} @0x{baseAddress:X16}",
            viewLength,
            pidIdentity?.VersionToken ?? $"pid:{processId};gone");
    }

    public ByteSourceIdentity Identity => _identity;

    /// <summary>进程内存只支持 Read/Overwrite/Refresh（规格 §4/§6）。</summary>
    public ByteSourceCapabilities Capabilities =>
        ByteSourceCapabilities.Read | ByteSourceCapabilities.Overwrite | ByteSourceCapabilities.Refresh;

    public ValueTask<ByteReadResult> ReadAsync(ulong offset, Memory<byte> destination, CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProcessMemoryByteSource));
        }
        if (destination.IsEmpty)
        {
            return ValueTask.FromResult(ByteReadResult.Full(0));
        }

        var requested = (int)Math.Min((ulong)destination.Length, _viewLength - Math.Min(offset, _viewLength));
        if (offset >= _viewLength || requested <= 0)
        {
            return ValueTask.FromResult(new ByteReadResult(0, [new ByteRange(offset, (ulong)destination.Length)], false));
        }

        lock (_gate)
        {
            var pidIdentity = ProcessIdentity.FromId(_processId);
            if (pidIdentity is null)
            {
                return ValueTask.FromResult(new ByteReadResult(0, [new ByteRange(offset, (ulong)destination.Length)], false));
            }
            using var handle = ProcessHandle.TryOpen(pidIdentity);
            if (handle is null)
            {
                return ValueTask.FromResult(new ByteReadResult(0, [new ByteRange(offset, (ulong)destination.Length)], false));
            }

            var unavailable = new List<ByteRange>();
            var completed = 0;
            var pos = offset;
            while (completed < requested)
            {
                ct.ThrowIfCancellationRequested();
                var take = Math.Min(ReadChunk, requested - completed);
                var read = handle.Read(checked(_baseAddress + pos), destination.Span[completed..(completed + take)]);
                if (read <= 0)
                {
                    // 不可读区：整块记入 UnavailableRanges
                    unavailable.Add(new ByteRange(pos, (ulong)take));
                    pos += (ulong)take;
                    completed += take;
                }
                else
                {
                    pos += (ulong)read;
                    completed += read;
                }
            }

            return ValueTask.FromResult(new ByteReadResult(completed, unavailable, false));
        }
    }

    public ValueTask<CommitResult> CommitAsync(ChangeSet changes, CommitOptions options, CancellationToken ct)
    {
        if (_disposed)
        {
            return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.IoError, "字节源已释放。"));
        }

        // 进程源只允许等长覆盖（规格 §6：插入/删除禁用）
        if (changes.Edits.Any(e => e is not OverwriteEdit))
        {
            return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.PermissionDenied,
                "进程内存只支持等长覆盖写入；插入/删除已禁用。"));
        }

        // 身份复验：目标重启后所有提交拒绝（规格 §6）
        if (options.VerifyVersionToken)
        {
            var current = ProcessIdentity.FromId(_processId);
            if (current is null || current.VersionToken != _identity.VersionToken)
            {
                return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.TargetProcessChanged,
                    "目标进程已重启或退出，写入被拒绝。"));
            }
        }

        lock (_gate)
        {
            var pidIdentity = ProcessIdentity.FromId(_processId);
            if (pidIdentity is null)
            {
                return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.TargetProcessChanged, "目标进程不可达。"));
            }
            using var handle = ProcessHandle.TryOpen(pidIdentity);
            if (handle is null)
            {
                return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.PermissionDenied, "无法打开目标进程。"));
            }

            foreach (var edit in changes.Edits)
            {
                ct.ThrowIfCancellationRequested();
                var overwrite = (OverwriteEdit)edit;

                // 写前比较：ExpectedCurrentValue = Before（文档打开时的原值）
                var ok = handle.CompareWriteAndVerify(
                    checked(_baseAddress + overwrite.Offset), overwrite.Before, overwrite.After);
                if (!ok)
                {
                    return ValueTask.FromResult(CommitResult.Fail(CommitErrorKind.ReadBackMismatch,
                        $"偏移 0x{overwrite.Offset:X16} 写入失败：当前值已变化或读回不一致。"));
                }
            }
        }

        return ValueTask.FromResult(CommitResult.Ok(_identity.VersionToken));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
