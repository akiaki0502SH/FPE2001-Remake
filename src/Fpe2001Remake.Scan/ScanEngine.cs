using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Memory.Win32;

namespace Fpe2001Remake.Scan;

/// <summary>
/// 扫描引擎（规格 §8）：首次/再次扫描、Mission、进度、取消、FPSN 快照。
/// 取消不是错误：保留上个完整快照。长任务全部后台执行并支持取消。
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    public const int ReadBlockSize = 1024 * 1024; // 1 MiB 读块

    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionState> _sessions = [];

    private sealed class SessionState
    {
        public required string MissionName;
        public required int ProcessId;
        public required ProcessIdentity Identity;
        public required ScanDataType DataType;
        public required Endianness Endianness;
        public required int ValueSize;
        public required ScanStepKind CurrentStep;
        public required string SnapshotPath;
        public CancellationTokenSource? RunningCts;
        public ulong CandidateCount;
    }

    public async ValueTask<ScanSession> BeginFirstScanAsync(
        int processId,
        string missionName,
        ScanCondition condition,
        LogicalAddress? rangeStart,
        LogicalAddress? rangeEnd,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        if (condition.Step != ScanStepKind.FirstScan)
        {
            throw new ArgumentException("首次扫描条件 Step 必须为 FirstScan。", nameof(condition));
        }
        if (condition.Comparison != ScanComparison.UnknownValue && condition.Value is null)
        {
            throw new ArgumentException("首次扫描必须提供输入值或未知值。", nameof(condition));
        }

        var identity = ProcessIdentity.FromId(processId)
            ?? throw new InvalidOperationException($"无法访问目标进程（PID {processId}）。");
        using var probe = ProcessHandle.TryOpen(identity)
            ?? throw new InvalidOperationException($"无法打开目标进程（PID {processId}）。请检查权限。");

        var dataType = condition.Value?.DataType ?? ScanDataType.Unknown;
        var endianness = condition.Value?.Endianness ?? Endianness.LittleEndian;
        var valueSize = condition.Comparison == ScanComparison.UnknownValue
            ? 1
            : ValueCodec.SizeOf(dataType);
        var alignment = condition.Value?.Alignment ?? 1;

        var sessionId = Guid.NewGuid();
        var snapshotDir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "snapshots");
        Directory.CreateDirectory(snapshotDir);
        var snapshotPath = Path.Combine(snapshotDir, $"{sessionId:N}.fpsn");

        var running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var session = new SessionState
        {
            MissionName = missionName,
            ProcessId = processId,
            Identity = identity,
            DataType = dataType,
            Endianness = endianness,
            ValueSize = valueSize,
            CurrentStep = ScanStepKind.FirstScan,
            SnapshotPath = snapshotPath,
            RunningCts = running,
        };
        lock (_gate)
        {
            _sessions[sessionId] = session;
        }

        try
        {
            var candidates = await Task.Run(
                () => RunFirstScan(session, condition, rangeStart, rangeEnd, alignment, progress, running.Token),
                running.Token);
            session.CandidateCount = (ulong)candidates;
            return new ScanSession(
                sessionId, missionName, processId, identity.ProcessName,
                ScanStepKind.FirstScan, candidates, snapshotPath);
        }
        catch (OperationCanceledException)
        {
            // 取消不是错误：保留快照（可能不完整但读取安全）
            return new ScanSession(
                sessionId, missionName, processId, identity.ProcessName,
                ScanStepKind.FirstScan, 0, snapshotPath);
        }
        finally
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out var s) && s.RunningCts == running)
                {
                    // 保留 session 供再次扫描；仅释放取消源
                }
            }
            running.Dispose();
        }
    }

    public async ValueTask<ScanSession> RunNextScanAsync(
        Guid sessionId,
        ScanCondition condition,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        if (condition.Step != ScanStepKind.NextScan)
        {
            throw new ArgumentException("再次扫描条件 Step 必须为 NextScan。", nameof(condition));
        }

        SessionState session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session!))
            {
                throw new InvalidOperationException("扫描会话不存在或已失效。");
            }
        }

        if (!session.Identity.Verify())
        {
            throw new InvalidOperationException("目标进程已重启或退出，扫描会话失效。");
        }

        var running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        session.RunningCts = running;

        try
        {
            var candidates = await Task.Run(
                () => RunNextScanCore(session, condition, progress, running.Token), running.Token);
            session.CandidateCount = (ulong)candidates;
            session.CurrentStep = ScanStepKind.NextScan;
            return new ScanSession(
                sessionId, session.MissionName, session.ProcessId, session.Identity.ProcessName,
                ScanStepKind.NextScan, candidates, session.SnapshotPath);
        }
        finally
        {
            session.RunningCts = null;
            running.Dispose();
        }
    }

    public ValueTask<ScanSession> CancelAsync(Guid sessionId, CancellationToken ct)
    {
        SessionState? session;
        lock (_gate)
        {
            _sessions.TryGetValue(sessionId, out session);
        }
        if (session is null)
        {
            return ValueTask.FromResult<ScanSession>(null!);
        }
        session.RunningCts?.Cancel();
        return ValueTask.FromResult(new ScanSession(
            sessionId, session.MissionName, session.ProcessId, session.Identity.ProcessName,
            session.CurrentStep, checked((long)session.CandidateCount), session.SnapshotPath));
    }

    public ValueTask<IReadOnlyList<ScanCandidate>> ReadCandidatesAsync(
        Guid sessionId, int offset, int count, CancellationToken ct)
    {
        SessionState session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out session!))
            {
                return ValueTask.FromResult<IReadOnlyList<ScanCandidate>>([]);
            }
        }

        var result = new List<ScanCandidate>(count);
        var stride = 8 + session.ValueSize;

        using var stream = File.OpenRead(session.SnapshotPath);
        stream.Seek(FpsnSnapshot.HeaderSize + (long)offset * stride, SeekOrigin.Begin);
        var record = new byte[stride];
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var read = stream.Read(record, 0, stride);
            if (read < stride)
            {
                break;
            }
            var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(record);
            var value = record.AsSpan(8, session.ValueSize).ToArray();
            result.Add(new ScanCandidate(
                new LogicalAddress(AddressSpace.HostVirtual, $"pid:{session.ProcessId}", address,
                    session.Endianness, (byte)(session.ValueSize * 8)),
                ValueCodec.Format(value, session.DataType, session.Endianness),
                null,
                $"pid:{session.ProcessId}",
                address));
        }

        return ValueTask.FromResult<IReadOnlyList<ScanCandidate>>(result);
    }

    // ---------- 后台执行体 ----------

    private static long RunFirstScan(
        SessionState session,
        ScanCondition condition,
        LogicalAddress? rangeStart,
        LogicalAddress? rangeEnd,
        ulong alignment,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        using var handle = ProcessHandle.TryOpen(session.Identity)
            ?? throw new InvalidOperationException("无法打开目标进程。");

        var start = rangeStart?.Value ?? 0UL;
        var end = rangeEnd?.Value ?? 0x0000FFFFFFFFFFFFUL;

        var regions = handle.EnumerateRegions(start, end)
            .Where(r => r.IsReadable)
            .Select(r =>
            {
                // 裁剪到扫描范围 [start, end]（区域可能跨出范围）
                var rStart = Math.Max(r.BaseAddress, start);
                ulong rEnd;
                try
                {
                    rEnd = checked(r.BaseAddress + r.RegionSize);
                }
                catch (OverflowException)
                {
                    rEnd = ulong.MaxValue;
                }
                rEnd = Math.Min(rEnd, end);
                return new ByteRange(rStart, rEnd > rStart ? rEnd - rStart : 0);
            })
            .Where(r => !r.IsEmpty)
            .ToList();

        var planned = regions.Aggregate(0UL, (acc, r) => checked(acc + r.Length));

        ulong readBytes = 0;
        long candidates = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        using var snapshot = FpsnSnapshot.Create(session.SnapshotPath, session.DataType, session.Endianness);
        var buffer = new byte[ReadBlockSize + session.ValueSize - 1];

        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();
            ScanRegion(handle, region, session, alignment, condition, buffer, snapshot,
                ref readBytes, ref candidates, progress, planned, regions.Count, watch, ct);
            progress?.Report(new ScanProgress(planned, readBytes, regions.Count, 0, candidates, 0, watch.Elapsed));
        }

        return candidates;
    }

    private static long RunNextScanCore(
        SessionState session,
        ScanCondition condition,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        using var handle = ProcessHandle.TryOpen(session.Identity)
            ?? throw new InvalidOperationException("无法重新打开目标进程。");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        long candidates = 0;
        ulong readBytes = 0;

        var oldPath = session.SnapshotPath;
        var newPath = oldPath + ".next";

        using (var old = FpsnSnapshot.Open(oldPath))
        using (var fresh = FpsnSnapshot.Create(newPath, session.DataType, session.Endianness))
        {
            var total = old.Count;
            // 块读取缓存：快照记录地址升序，几乎顺序命中（规格 §4：编辑器分页缓存同思路）
            var block = new byte[ReadBlockSize + session.ValueSize - 1];
            ulong blockBase = 0;
            int blockLen = 0;

            foreach (var (address, previous) in old.ReadAll())
            {
                ct.ThrowIfCancellationRequested();

                if (address < blockBase || address + (ulong)session.ValueSize > blockBase + (ulong)blockLen)
                {
                    blockBase = address;
                    blockLen = handle.Read(address, block);
                }

                if (address + (ulong)session.ValueSize > blockBase + (ulong)blockLen)
                {
                    continue; // 区域不可读：保守丢弃
                }

                var current = block.AsSpan((int)(address - blockBase), session.ValueSize);
                readBytes += (ulong)session.ValueSize;

                if (ValueComparer.Matches(condition, previous, current.ToArray(), session.DataType, session.Endianness))
                {
                    fresh.Append(address, current);
                    candidates++;
                }

                if ((candidates & 0x3FFF) == 0)
                {
                    progress?.Report(new ScanProgress(total, (ulong)candidates, 1, 0, candidates, 0, watch.Elapsed));
                }
            }
        }

        // 原子替换：旧快照被新快照取代
        File.Move(newPath, oldPath, overwrite: true);

        return candidates;
    }

    private static void ScanRegion(
        ProcessHandle handle,
        ByteRange region,
        SessionState session,
        ulong alignment,
        ScanCondition condition,
        byte[] buffer,
        FpsnSnapshot snapshot,
        ref ulong readBytes,
        ref long candidates,
        IProgress<ScanProgress>? progress,
        ulong planned,
        long regionCount,
        System.Diagnostics.Stopwatch watch,
        CancellationToken ct)
    {
        var valueSize = session.ValueSize;
        var regionEnd = region.EndExclusive;
        var position = region.Offset;
        var tail = 0;

        while (position < regionEnd)
        {
            ct.ThrowIfCancellationRequested();

            var toRead = (int)Math.Min((ulong)buffer.Length - (ulong)tail, regionEnd - position);
            var read = handle.Read(position, buffer.AsSpan(tail, toRead));
            if (read <= 0)
            {
                // 区域不可读/读取失败：跳过该区域剩余部分（规格：访问拒绝为正常状态）
                break;
            }
            readBytes += (ulong)read;

            var window = tail + read;
            var globalStart = position - (ulong)tail;
            var start = 0;
            if (alignment > 1)
            {
                var alignedOffset = (alignment - (globalStart % alignment)) % alignment;
                if (alignedOffset < (ulong)window)
                {
                    start = (int)alignedOffset;
                }
            }

            var stride = checked((int)alignment);
            for (var i = start; i + valueSize <= window; i += stride)
            {
                var address = globalStart + (ulong)i;
                var current = buffer.AsSpan(i, valueSize);
                if (ValueComparer.MatchesFirst(condition, current.ToArray(), session.DataType, session.Endianness))
                {
                    snapshot.Append(address, current);
                    candidates++;
                }
            }

            // 保留尾部重叠（跨块候选）
            tail = Math.Min(valueSize - 1, window);
            if (tail > 0)
            {
                Buffer.BlockCopy(buffer, window - tail, buffer, 0, tail);
            }

            position += (ulong)read;
            progress?.Report(new ScanProgress(planned, readBytes, regionCount, 0, candidates, 0, watch.Elapsed));
        }
    }
}
