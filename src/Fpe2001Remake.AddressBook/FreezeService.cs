using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Memory.Win32;
using Fpe2001Remake.Scan;

namespace Fpe2001Remake.AddressBook;

/// <summary>
/// 冻结服务（规格 §9）：单调度器、最短 16ms、默认 250ms、连续失败停用、500ms 全停。
/// 条件：Always/Equals/NotEquals/InRange/Changed。目标重启后所有写入拒绝。
/// </summary>
public sealed class FreezeService : IFreezeService, IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(16);
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);
    public static readonly int DefaultMaxConsecutiveFailures = 5;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, FreezeItem> _items = [];
    private readonly CancellationTokenSource _loopCts = new();
    private readonly Func<Guid, AddressBookEntry?> _entryResolver;
    private readonly Func<Guid, ProcessIdentity?> _identityResolver;
    private Task? _loopTask;

    private sealed class FreezeItem
    {
        public required FreezeSpec Spec;
        public required AddressBookEntry Entry;
        public long WriteCount;
        public long FailureCount;
        public DateTimeOffset NextDueUtc;
        public string? LastError;
        public byte[]? PreviousValue;
    }

    public FreezeService(Func<Guid, AddressBookEntry?> entryResolver, Func<Guid, ProcessIdentity?>? identityResolver = null)
    {
        _entryResolver = entryResolver;
        _identityResolver = identityResolver ?? (id =>
        {
            var entry = entryResolver(id);
            var pid = entry is null ? -1 : AddressBookService.ParsePid(entry.Address.DomainId);
            return pid >= 0 ? ProcessIdentity.FromId(pid) : null;
        });
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
    }

    public ValueTask<FreezeStatus> StartAsync(FreezeSpec spec, CancellationToken ct)
    {
        var interval = spec.Interval <= TimeSpan.Zero ? DefaultInterval
            : spec.Interval < MinInterval ? MinInterval : spec.Interval;

        var entry = _entryResolver(spec.EntryId)
            ?? throw new InvalidOperationException("冻结条目不存在于地址表。");

        lock (_gate)
        {
            _items[spec.EntryId] = new FreezeItem
            {
                Spec = spec with { Interval = interval },
                Entry = entry,
                NextDueUtc = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50),
            };
        }
        return ValueTask.FromResult(new FreezeStatus(spec.EntryId, true, 0, 0, null, interval));
    }

    public ValueTask<FreezeStatus> StopAsync(Guid entryId, CancellationToken ct)
    {
        FreezeItem? item;
        lock (_gate)
        {
            _items.Remove(entryId, out item);
        }
        if (item is null)
        {
            return ValueTask.FromResult(new FreezeStatus(entryId, false, 0, 0, "未在冻结", TimeSpan.Zero));
        }
        return ValueTask.FromResult(new FreezeStatus(
            entryId, false, item.WriteCount, item.FailureCount, null, item.Spec.Interval));
    }

    public ValueTask StopAllAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _items.Clear();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<FreezeStatus>> ListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<FreezeStatus>>(
                _items.Values.Select(i => new FreezeStatus(
                    i.Spec.EntryId, true, i.WriteCount, i.FailureCount, i.LastError, i.Spec.Interval)).ToList());
        }
    }

    public void Dispose()
    {
        _loopCts.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
        _loopCts.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------- 调度循环 ----------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(50, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            List<FreezeItem> due;
            lock (_gate)
            {
                due = _items.Values.Where(i => i.NextDueUtc <= now).ToList();
            }

            foreach (var item in due)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    Tick(item, now);
                }
                catch
                {
                    // 单条目失败不影响调度器
                }
            }
        }
    }

    private void Tick(FreezeItem item, DateTimeOffset now)
    {
        if (!item.Entry.Enabled)
        {
            item.LastError = "条目已停用";
            return;
        }

        // 熔断检查
        if (item.FailureCount >= (item.Spec.MaxConsecutiveFailures > 0 ? item.Spec.MaxConsecutiveFailures : DefaultMaxConsecutiveFailures))
        {
            item.LastError = $"连续失败 {item.FailureCount} 次，已停用";
            return;
        }

        // 条件判断需要当前值
        var pid = AddressBookService.ParsePid(item.Entry.Address.DomainId);
        if (pid < 0)
        {
            item.LastError = "无效域（无 PID）";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        var identity = _identityResolver(item.Entry.Id);
        if (identity is null || !identity.Verify())
        {
            item.LastError = "目标进程不存在或已重启";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        using var handle = ProcessHandle.TryOpen(identity);
        if (handle is null)
        {
            item.LastError = "无法打开目标进程";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        var width = AddressBookService.ValueCodecWidth(item.Entry.DataType);
        var current = new byte[width];
        var read = handle.Read(item.Entry.Address.Value, current);
        if (read != width)
        {
            item.LastError = "区域不可读";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        if (!ConditionAllowsWrite(item, current))
        {
            item.PreviousValue = current;
            item.FailureCount = 0; // 条件不满足不是失败
            ScheduleNext(item, now);
            return;
        }

        // 写入目标值：优先 FreezeSpec.Value，其次条目 WriteValueText
        var targetText = item.Spec.Value.HasValue
            ? FormatSpecValue(item, item.Spec.Value.Value)
            : item.Entry.WriteValueText;
        if (targetText is null)
        {
            item.LastError = "缺少写入值";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        var spec = new ScanValueSpec(item.Entry.DataType, targetText, item.Entry.Address.Endianness);
        if (!ValueCodec.TryEncode(spec, out var newValue) || newValue.Length != width)
        {
            item.LastError = "写入值无法编码";
            item.FailureCount++;
            ScheduleNext(item, now);
            return;
        }

        // 写前比较：当前值 == 目标值 则无需写入
        if (current.AsSpan().SequenceEqual(newValue))
        {
            item.PreviousValue = current;
            item.FailureCount = 0;
            ScheduleNext(item, now);
            return;
        }

        var ok = handle.CompareWriteAndVerify(item.Entry.Address.Value, current, newValue);
        if (ok)
        {
            item.WriteCount++;
            item.FailureCount = 0;
            item.LastError = null;
        }
        else
        {
            item.FailureCount++;
            item.LastError = "写入失败（写前比较或读回校验未通过）";
        }
        item.PreviousValue = current;

        ScheduleNext(item, now);
    }

    private static bool ConditionAllowsWrite(FreezeItem item, byte[] current)
    {
        var value = ValueCodec.ToUInt64(current, item.Entry.Address.Endianness);
        var type = item.Entry.DataType;
        double dvalue = type is ScanDataType.Float32 or ScanDataType.Float64
            ? ValueCodec.ToDouble(current, type, item.Entry.Address.Endianness) : value;

        return item.Spec.Kind switch
        {
            FreezeConditionKind.Always => true,
            FreezeConditionKind.Equals => item.Spec.Value.HasValue && value == item.Spec.Value.Value,
            FreezeConditionKind.NotEquals => !item.Spec.Value.HasValue || value != item.Spec.Value.Value,
            FreezeConditionKind.InRange =>
                item.Spec.RangeStart.HasValue && item.Spec.RangeEnd.HasValue &&
                value >= item.Spec.RangeStart.Value && value <= item.Spec.RangeEnd.Value,
            FreezeConditionKind.Changed => item.PreviousValue is not null &&
                                           !item.PreviousValue.AsSpan().SequenceEqual(current),
            _ => false,
        };
    }

    private static string FormatSpecValue(FreezeItem item, ulong value)
    {
        return item.Entry.DataType switch
        {
            ScanDataType.UInt8 => ((byte)value).ToString(),
            ScanDataType.UInt16 => ((ushort)value).ToString(),
            ScanDataType.UInt32 => ((uint)value).ToString(),
            _ => value.ToString(),
        };
    }

    private void ScheduleNext(FreezeItem item, DateTimeOffset now)
    {
        item.NextDueUtc = now + item.Spec.Interval;
    }
}
