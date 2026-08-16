using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Memory.Win32;
using Fpe2001Remake.Scan;

namespace Fpe2001Remake.AddressBook;

/// <summary>
/// 地址表服务（规格 §8）：条目、分组、注释、值刷新、立即写入。
/// 逻辑地址优先；跨会话不持久化宿主地址（P3 落 SQLite，当前为内存实现）。
/// 写入三要素：身份复验 + 写前比较 + 读回校验（规格 §6）。
/// </summary>
public sealed class AddressBookService : IAddressBookService
{
    private readonly object _gate = new();
    private readonly List<AddressBookEntry> _entries = [];
    // 地址是绑定到一次扫描/添加时的进程实例，而非仅绑定会被复用的 PID。
    private readonly Dictionary<Guid, ProcessIdentity> _targetIdentities = [];

    /// <summary>条目更新（值刷新/写入后触发，UI 订阅刷新视图）。</summary>
    public event Action<Guid>? EntryChanged;

    public ValueTask<IReadOnlyList<AddressBookEntry>> ListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<AddressBookEntry>>(_entries.ToList());
        }
    }

    /// <summary>按 Id 查询条目（冻结服务解析器用）。</summary>
    public AddressBookEntry? GetEntryById(Guid entryId)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.Id == entryId);
        }
    }

    public ProcessIdentity? GetTargetIdentity(Guid entryId)
    {
        lock (_gate)
        {
            return _targetIdentities.TryGetValue(entryId, out var identity) ? identity : null;
        }
    }

    public ValueTask<AddressBookEntry> UpsertAsync(AddressBookEntry entry, CancellationToken ct)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Id == entry.Id);
            if (index >= 0)
            {
                _entries[index] = entry;
            }
            else
            {
                _entries.Add(entry);
                var pid = ParsePid(entry.Address.DomainId);
                var identity = pid >= 0 ? ProcessIdentity.FromId(pid) : null;
                if (identity is not null) _targetIdentities[entry.Id] = identity;
            }
        }
        EntryChanged?.Invoke(entry.Id);
        return ValueTask.FromResult(entry);
    }

    public ValueTask<bool> RemoveAsync(Guid entryId, CancellationToken ct)
    {
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => e.Id == entryId) > 0;
            _targetIdentities.Remove(entryId);
            if (removed)
            {
                EntryChanged?.Invoke(entryId);
            }
            return ValueTask.FromResult(removed);
        }
    }

    public ValueTask ClearAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _entries.Clear();
            _targetIdentities.Clear();
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> WriteNowAsync(Guid entryId, string? valueText, CancellationToken ct)
    {
        AddressBookEntry? entry;
        lock (_gate)
        {
            entry = _entries.FirstOrDefault(e => e.Id == entryId);
        }
        if (entry is null)
        {
            return false;
        }

        var success = await WriteEntryAsync(entry, valueText ?? entry.WriteValueText, ct);
        if (success)
        {
            var refreshed = entry with { WriteValueText = valueText ?? entry.WriteValueText };
            lock (_gate)
            {
                var idx = _entries.FindIndex(e => e.Id == entryId);
                if (idx >= 0) _entries[idx] = refreshed;
            }
            EntryChanged?.Invoke(entryId);
        }
        return success;
    }

    public async ValueTask RefreshValuesAsync(Guid entryId, CancellationToken ct)
    {
        AddressBookEntry? entry;
        lock (_gate)
        {
            entry = _entries.FirstOrDefault(e => e.Id == entryId);
        }
        if (entry is null) return;

        var current = await ReadCurrentAsync(entry, ct);
        if (current is not null)
        {
            lock (_gate)
            {
                var idx = _entries.FindIndex(e => e.Id == entryId);
                if (idx >= 0)
                {
                    _entries[idx] = entry with { CurrentValueText = current };
                }
            }
            EntryChanged?.Invoke(entryId);
        }
    }

    /// <summary>读取条目当前值（供 UI/冻结服务使用）。</summary>
    public async ValueTask<string?> ReadCurrentAsync(AddressBookEntry entry, CancellationToken ct)
    {
        if (!TryGetVerifiedIdentity(entry.Id, out var identity)) return null;
        using var handle = ProcessHandle.TryOpen(identity);
        if (handle is null) return null;

        var value = new byte[ValueCodecWidth(entry.DataType)];
        var read = handle.Read(entry.Address.Value, value);
        if (read != value.Length) return null;
        return FormatValue(value, entry.DataType, entry.Address.Endianness);
    }

    internal static int ParsePid(string domainId)
    {
        if (domainId.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(domainId.AsSpan(4), out var pid))
        {
            return pid;
        }
        return -1;
    }

    internal static int ValueCodecWidth(ScanDataType type) => type switch
    {
        ScanDataType.UInt8 => 1,
        ScanDataType.UInt16 => 2,
        ScanDataType.UInt32 => 4,
        ScanDataType.UInt64 => 8,
        ScanDataType.Float32 => 4,
        ScanDataType.Float64 => 8,
        _ => 1,
    };

    internal static string FormatValue(byte[] value, ScanDataType type, Endianness endianness)
    {
        return type switch
        {
            ScanDataType.UInt8 => value[0].ToString(),
            ScanDataType.UInt16 => Convert16(value, endianness).ToString(),
            ScanDataType.UInt32 => Convert32(value, endianness).ToString(),
            ScanDataType.UInt64 => Convert64(value, endianness).ToString(),
            ScanDataType.Float32 => BitConverter.Int32BitsToSingle((int)Convert32(value, endianness)).ToString("G9"),
            ScanDataType.Float64 => BitConverter.Int64BitsToDouble((long)Convert64(value, endianness)).ToString("G17"),
            _ => Convert.ToHexString(value),
        };
    }

    private static ushort Convert16(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt16(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(value, 0));

    private static uint Convert32(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt32(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt32(value, 0));

    private static ulong Convert64(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt64(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(value, 0));

    /// <summary>编码写入值并执行 CompareWriteAndVerify。</summary>
    private bool TryGetVerifiedIdentity(Guid entryId, out ProcessIdentity identity)
    {
        lock (_gate)
        {
            if (!_targetIdentities.TryGetValue(entryId, out identity!)) return false;
        }
        return identity.Verify();
    }

    internal async ValueTask<bool> WriteEntryAsync(AddressBookEntry entry, string? valueText, CancellationToken ct)
    {
        if (valueText is null) return false;
        if (!TryGetVerifiedIdentity(entry.Id, out var identity)) return false;

        using var handle = ProcessHandle.TryOpen(identity);
        if (handle is null) return false;

        var spec = new ScanValueSpec(entry.DataType, valueText, entry.Address.Endianness);
        if (!ValueCodec.TryEncode(spec, out var newValue))
        {
            return false;
        }

        // 写前比较失败时必须保守拒绝，不能降级为盲写。
        Span<byte> expected = stackalloc byte[newValue.Length];
        var read = handle.Read(entry.Address.Value, expected);
        if (read != newValue.Length)
        {
            return false;
        }

        return handle.CompareWriteAndVerify(entry.Address.Value, expected[..read], newValue);
    }
}
