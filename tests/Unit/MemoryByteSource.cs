using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.UnitTests;

/// <summary>内存字节源（单元测试辅助）：byte[] 包装，支持完整编辑能力。</summary>
public sealed class MemoryByteSource : IEditableByteSource
{
    private readonly byte[] _data;
    private readonly ByteSourceIdentity _identity;

    public MemoryByteSource(byte[] data, string versionToken = "mem:v1")
    {
        _data = data;
        _identity = new ByteSourceIdentity(Guid.NewGuid(), ByteSourceKind.Snapshot, "memory", (ulong)data.Length, versionToken);
    }

    public ByteSourceIdentity Identity => _identity;

    public ByteSourceCapabilities Capabilities =>
        ByteSourceCapabilities.Read | ByteSourceCapabilities.Overwrite |
        ByteSourceCapabilities.Insert | ByteSourceCapabilities.Delete |
        ByteSourceCapabilities.KnownLength | ByteSourceCapabilities.AtomicCommit;

    public byte[] Data => _data;

    public ValueTask<ByteReadResult> ReadAsync(ulong offset, Memory<byte> destination, CancellationToken ct)
    {
        if (offset >= (ulong)_data.Length)
        {
            return ValueTask.FromResult(new ByteReadResult(0, [new ByteRange(offset, (ulong)destination.Length)], false));
        }
        var count = (int)Math.Min((ulong)destination.Length, (ulong)_data.Length - offset);
        _data.AsMemory((int)offset, count).CopyTo(destination);
        return ValueTask.FromResult(ByteReadResult.Full(count));
    }

    public ValueTask<CommitResult> CommitAsync(ChangeSet changes, CommitOptions options, CancellationToken ct)
    {
        // 简单内存应用（测试辅助）
        var buffer = new List<byte>(_data);
        foreach (var edit in changes.Edits.OrderBy(e => e.Offset))
        {
            switch (edit)
            {
                case OverwriteEdit o:
                    for (var i = 0; i < o.After.Length; i++)
                    {
                        buffer[checked((int)(o.Offset + (ulong)i))] = o.After[i];
                    }
                    break;
                case InsertEdit i:
                    buffer.InsertRange(checked((int)i.Offset), i.Data);
                    break;
                case DeleteEdit d:
                    buffer.RemoveRange(checked((int)d.Offset), d.Deleted.Length);
                    break;
            }
        }
        buffer.CopyTo(_data.AsSpan(0, Math.Min(_data.Length, buffer.Count)));
        return ValueTask.FromResult(CommitResult.Ok(_identity.VersionToken));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
