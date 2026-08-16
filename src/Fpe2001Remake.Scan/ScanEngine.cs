using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Memory.Win32;

namespace Fpe2001Remake.Scan;

/// <summary>
/// 扫描引擎（规格 §8）：首次/再次扫描、Mission、进度、取消、FPSN 快照。
/// 取消不是错误：保留上个完整快照。长任务全部后台执行并支持取消。
/// 自动模式（ScanDataType.Auto）：首次按 8/16/32 位同时扫描（每类型独立快照），
/// 再次扫描时各快照独立过滤，自动收敛出真实类型——解决模拟器游戏"类型没选对"搜不到的问题。
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    public const int ReadBlockSize = 1024 * 1024; // 1 MiB 读块

    /// <summary>进度节流间隔：进度回调用太多会压垮 UI 线程，200ms 一次足够。</summary>
    private const int ProgressIntervalMs = 200;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionState> _sessions = [];

    private sealed class SessionState
    {
        public required Guid Id;
        public required string MissionName;
        public required int ProcessId;
        public required ProcessIdentity Identity;
        public required bool IsAuto;
        public required ScanDataType[] DataTypes;   // 自动模式 = [UInt8, UInt16, UInt32]；否则 = [type]
        public required int[] ValueSizes;           // 与 DataTypes 一一对应
        public required string[] SnapshotPaths;     // 与 DataTypes 一一对应
        public required Endianness Endianness;
        public required ScanStepKind CurrentStep;
        public CancellationTokenSource? RunningCts;
        public ulong CandidateCount;

        public string PrimarySnapshotPath => SnapshotPaths[0];
    }

    /// <summary>构造会话并统计各快照候选数（供 UI 显示 8/16/32 位命中分布，诊断类型问题）。</summary>
    private static ScanSession BuildSession(SessionState session, ScanStepKind step, long candidates)
    {
        var stats = new List<ScanSnapshotInfo>(session.SnapshotPaths.Length);
        foreach (var path in session.SnapshotPaths)
        {
            try
            {
                using var snap = FpsnSnapshot.Open(path);
                stats.Add(new ScanSnapshotInfo(snap.DataType, (long)snap.Count));
            }
            catch
            {
                stats.Add(new ScanSnapshotInfo(ScanDataType.Unknown, 0));
            }
        }
        return new ScanSession(
            session.Id, session.MissionName, session.ProcessId, session.Identity.ProcessName,
            step, candidates, session.PrimarySnapshotPath, stats);
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
        var isAuto = dataType == ScanDataType.Auto;
        var alignment = condition.Value?.Alignment ?? 1;

        ScanDataType[] types = isAuto
            ? [ScanDataType.UInt8, ScanDataType.UInt16, ScanDataType.UInt32]
            : [dataType];
        var valueSizes = types
            .Select(t => t == ScanDataType.Unknown ? 1 : ValueCodec.SizeOf(t))
            .ToArray();

        var sessionId = Guid.NewGuid();
        var snapshotDir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "snapshots");
        Directory.CreateDirectory(snapshotDir);
        var snapshotPath = Path.Combine(snapshotDir, $"{sessionId:N}.fpsn");
        // 自动模式：8 位主快照 + 16/32 位后缀快照
        string[] snapshotPaths = isAuto
            ? [snapshotPath, snapshotPath + ".16", snapshotPath + ".32"]
            : [snapshotPath];

        var running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var session = new SessionState
        {
            Id = sessionId,
            MissionName = missionName,
            ProcessId = processId,
            Identity = identity,
            IsAuto = isAuto,
            DataTypes = types,
            ValueSizes = valueSizes,
            SnapshotPaths = snapshotPaths,
            Endianness = endianness,
            CurrentStep = ScanStepKind.FirstScan,
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
            return BuildSession(session, ScanStepKind.FirstScan, candidates);
        }
        catch (OperationCanceledException)
        {
            // 取消不是错误：保留快照（可能不完整但读取安全）
            return BuildSession(session, ScanStepKind.FirstScan, 0);
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
            return BuildSession(session, ScanStepKind.NextScan, candidates);
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
        return ValueTask.FromResult(BuildSession(session, session.CurrentStep, checked((long)session.CandidateCount)));
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
        var toSkip = (long)offset;

        // 自动模式展示顺序：32 → 16 → 8（真实地址更可能在宽类型列表里先出现）
        for (var idx = session.SnapshotPaths.Length - 1; idx >= 0 && result.Count < count; idx--)
        {
            using var snapshot = FpsnSnapshot.Open(session.SnapshotPaths[idx]);
            snapshot.ReadAllBulk((address, value) =>
            {
                ct.ThrowIfCancellationRequested();
                if (toSkip > 0)
                {
                    toSkip--;
                    return true;
                }
                if (result.Count >= count)
                {
                    return false;
                }
                result.Add(new ScanCandidate(
                    new LogicalAddress(AddressSpace.HostVirtual, $"pid:{session.ProcessId}", address,
                        session.Endianness, (byte)(snapshot.ValueSize * 8)),
                    ValueCodec.Format(value.ToArray(), snapshot.DataType, session.Endianness),
                    null,
                    $"pid:{session.ProcessId}",
                    address));
                return true;
            });
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

        if (session.IsAuto)
        {
            // 自动模式：内存只读一遍，8/16/32 位三宽度同时判定（原版 FPE 自动扫描语义）
            var snapshots = new FpsnSnapshot[session.SnapshotPaths.Length];
            var expecteds = new ExpectedValue?[session.DataTypes.Length];
            for (var i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = FpsnSnapshot.Create(session.SnapshotPaths[i], session.DataTypes[i], session.Endianness);
                expecteds[i] = ValueComparer.CompileExpected(condition, session.DataTypes[i], session.Endianness);
            }
            try
            {
                var buffer = new byte[ReadBlockSize + 4 - 1]; // 最大宽度 4
                foreach (var region in regions)
                {
                    ct.ThrowIfCancellationRequested();
                    ScanRegionAuto(handle, region, session, condition, expecteds, buffer, snapshots,
                        ref readBytes, ref candidates, progress, planned, regions.Count, watch, ct);
                }
            }
            finally
            {
                foreach (var s in snapshots)
                {
                    s.Dispose();
                }
            }
        }
        else
        {
            using var snapshot = FpsnSnapshot.Create(session.SnapshotPaths[0], session.DataTypes[0], session.Endianness);
            var valueSize = session.ValueSizes[0];
            var expected = ValueComparer.CompileExpected(condition, session.DataTypes[0], session.Endianness);
            var buffer = new byte[ReadBlockSize + valueSize - 1];
            foreach (var region in regions)
            {
                ct.ThrowIfCancellationRequested();
                ScanRegion(handle, region, valueSize, session.DataTypes[0], session.Endianness,
                    alignment, condition, expected, buffer, snapshot,
                    ref readBytes, ref candidates, progress, planned, regions.Count, watch, ct);
            }
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

        foreach (var snapshotPath in session.SnapshotPaths)
        {
            ct.ThrowIfCancellationRequested();
            candidates += RunNextScanForSnapshot(handle, snapshotPath, condition, progress, watch, ct);
        }

        return candidates;
    }

    private static long RunNextScanForSnapshot(
        ProcessHandle handle,
        string oldPath,
        ScanCondition condition,
        IProgress<ScanProgress>? progress,
        System.Diagnostics.Stopwatch watch,
        CancellationToken ct)
    {
        long candidates = 0;
        ulong readBytes = 0;
        var newPath = oldPath + ".next";

        using (var old = FpsnSnapshot.Open(oldPath))
        using (var fresh = FpsnSnapshot.Create(newPath, old.DataType, old.Endianness))
        {
            var valueSize = old.ValueSize;
            var total = old.Count;
            var expected = ValueComparer.CompileExpected(condition, old.DataType, old.Endianness);
            // 块读取缓存：快照记录地址升序，几乎顺序命中（规格 §4：编辑器分页缓存同思路）
            var block = new byte[ReadBlockSize + valueSize - 1];
            ulong blockBase = 0;
            int blockLen = 0;
            var lastReportMs = 0L;

            old.ReadAllBulk((address, previous) =>
            {
                ct.ThrowIfCancellationRequested();

                if (address < blockBase || address + (ulong)valueSize > blockBase + (ulong)blockLen)
                {
                    blockBase = address;
                    blockLen = ReadBlockWithRetry(handle, address, block);
                }

                if (address + (ulong)valueSize > blockBase + (ulong)blockLen)
                {
                    return true; // 区域不可读：保守丢弃
                }

                var current = block.AsSpan((int)(address - blockBase), valueSize);
                readBytes += (ulong)valueSize;

                if (ValueComparer.Matches(condition, expected, previous, current, old.DataType, old.Endianness))
                {
                    fresh.Append(address, current);
                    candidates++;
                }

                var nowMs = watch.ElapsedMilliseconds;
                if (progress is not null && nowMs - lastReportMs >= ProgressIntervalMs)
                {
                    lastReportMs = nowMs;
                    progress.Report(new ScanProgress(total, readBytes, 1, 0, candidates, 0, watch.Elapsed));
                }
                return true;
            });
        }

        // 原子替换：旧快照被新快照取代
        File.Move(newPath, oldPath, overwrite: true);

        return candidates;
    }

    /// <summary>
    /// 再次扫描的块读取：候选地址位于 < 1 MiB 的小区域（模拟器 WRAM、堆块等）时，
    /// 1 MiB 读会跨越区域边界导致 ReadProcessMemory 整体失败、该区域全部候选被丢弃。
    /// 失败后从 512 KiB 起逐级缩小重试，保证小区域内候选可读；缩到 4 KiB 仍失败视为不可读。
    /// </summary>
    private static int ReadBlockWithRetry(ProcessHandle handle, ulong address, Span<byte> block)
    {
        var n = handle.Read(address, block);
        if (n > 0)
        {
            return n;
        }
        var size = ReadBlockSize / 2;
        while (size >= 4096)
        {
            n = handle.Read(address, block[..size]);
            if (n > 0)
            {
                return n;
            }
            size /= 2;
        }
        return 0;
    }

    /// <summary>单类型区域扫描（保持原逻辑：可选对齐、宽度 1/2/4/8）。</summary>
    private static void ScanRegion(
        ProcessHandle handle,
        ByteRange region,
        int valueSize,
        ScanDataType dataType,
        Endianness endianness,
        ulong alignment,
        ScanCondition condition,
        ExpectedValue? expected,
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
        var regionEnd = region.EndExclusive;
        var position = region.Offset;
        var tail = 0;
        var lastReportMs = 0L;

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
                if (ValueComparer.MatchesFirst(condition, expected, current, dataType, endianness))
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

            var nowMs = watch.ElapsedMilliseconds;
            if (progress is not null && nowMs - lastReportMs >= ProgressIntervalMs)
            {
                lastReportMs = nowMs;
                progress.Report(new ScanProgress(planned, readBytes, regionCount, 0, candidates, 0, watch.Elapsed));
            }
        }
    }

    /// <summary>
    /// 自动模式区域扫描：内存只读一遍，同一块内按 8/16/32 位全偏移（stride 1）分别判定并写入各自快照。
    /// 宽度 1 跳过尾部重叠区（上块已判定）；宽度 2/4 从重叠区起点开始（跨块候选不遗漏）。
    /// </summary>
    private static void ScanRegionAuto(
        ProcessHandle handle,
        ByteRange region,
        SessionState session,
        ScanCondition condition,
        ExpectedValue?[] expecteds,
        byte[] buffer,
        FpsnSnapshot[] snapshots,
        ref ulong readBytes,
        ref long candidates,
        IProgress<ScanProgress>? progress,
        ulong planned,
        long regionCount,
        System.Diagnostics.Stopwatch watch,
        CancellationToken ct)
    {
        var regionEnd = region.EndExclusive;
        var position = region.Offset;
        var tail = 0;
        var lastReportMs = 0L;

        while (position < regionEnd)
        {
            ct.ThrowIfCancellationRequested();

            var toRead = (int)Math.Min((ulong)buffer.Length - (ulong)tail, regionEnd - position);
            var read = handle.Read(position, buffer.AsSpan(tail, toRead));
            if (read <= 0)
            {
                break;
            }
            readBytes += (ulong)read;

            var window = tail + read;
            var globalStart = position - (ulong)tail;

            // 8 位：跳过重叠区（tail 字节上块已全部判定）
            ScanAutoWidth(buffer, tail, window, 1, globalStart,
                snapshots[0], condition, expecteds[0], session.DataTypes[0], session.Endianness, ref candidates);
            // 16 位：从重叠区起点开始（跨块窗口不遗漏）
            ScanAutoWidth(buffer, 0, window, 2, globalStart,
                snapshots[1], condition, expecteds[1], session.DataTypes[1], session.Endianness, ref candidates);
            // 32 位
            ScanAutoWidth(buffer, 0, window, 4, globalStart,
                snapshots[2], condition, expecteds[2], session.DataTypes[2], session.Endianness, ref candidates);

            // 保留尾部重叠（最大宽度 4 的前 3 字节）
            tail = Math.Min(4 - 1, window);
            if (tail > 0)
            {
                Buffer.BlockCopy(buffer, window - tail, buffer, 0, tail);
            }

            position += (ulong)read;

            var nowMs = watch.ElapsedMilliseconds;
            if (progress is not null && nowMs - lastReportMs >= ProgressIntervalMs)
            {
                lastReportMs = nowMs;
                progress.Report(new ScanProgress(planned, readBytes, regionCount, 0, candidates, 0, watch.Elapsed));
            }
        }
    }

    /// <summary>自动模式单宽度扫描：宽度特化热循环（避免泛化调用链，实测提速显著）。</summary>
    private static void ScanAutoWidth(
        byte[] buffer,
        int start,
        int window,
        int width,
        ulong globalStart,
        FpsnSnapshot snapshot,
        ScanCondition condition,
        ExpectedValue? expected,
        ScanDataType dataType,
        Endianness endianness,
        ref long candidates)
    {
        if (expected is null || expected.Value.IsFloat)
        {
            // 未知值/浮点不进入自动模式（自动模式仅整数三宽度）；未知值走通用路径
            var last = window - width;
            for (var i = start; i <= last; i++)
            {
                if (ValueComparer.MatchesFirst(condition, expected, buffer.AsSpan(i, width), dataType, endianness))
                {
                    snapshot.Append(globalStart + (ulong)i, buffer.AsSpan(i, width));
                    candidates++;
                }
            }
            return;
        }

        var target = expected.Value.UInt;
        switch (width)
        {
            case 1 when endianness == Endianness.LittleEndian:
            {
                var b = (byte)target;
                var last = window - 1;
                for (var i = start; i <= last; i++)
                {
                    if (buffer[i] == b)
                    {
                        snapshot.Append(globalStart + (ulong)i, buffer.AsSpan(i, 1));
                        candidates++;
                    }
                }
                break;
            }
            case 2 when endianness == Endianness.LittleEndian:
            {
                var v = (ushort)target;
                var last = window - 2;
                for (var i = start; i <= last; i++)
                {
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i, 2)) == v)
                    {
                        snapshot.Append(globalStart + (ulong)i, buffer.AsSpan(i, 2));
                        candidates++;
                    }
                }
                break;
            }
            case 4 when endianness == Endianness.LittleEndian:
            {
                var v = (uint)target;
                var last = window - 4;
                for (var i = start; i <= last; i++)
                {
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i, 4)) == v)
                    {
                        snapshot.Append(globalStart + (ulong)i, buffer.AsSpan(i, 4));
                        candidates++;
                    }
                }
                break;
            }
            default:
            {
                // 大端或非整数：走通用比较器
                var last = window - width;
                for (var i = start; i <= last; i++)
                {
                    if (ValueComparer.MatchesFirst(condition, expected, buffer.AsSpan(i, width), dataType, endianness))
                    {
                        snapshot.Append(globalStart + (ulong)i, buffer.AsSpan(i, width));
                        candidates++;
                    }
                }
                break;
            }
        }
    }
}
