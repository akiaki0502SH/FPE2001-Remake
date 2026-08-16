using System.Diagnostics;
using System.Globalization;
using Fpe2001Remake.AddressBook;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Scan;
using Xunit;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>
/// P1 验收闭环（规格 15.1）：扫描→结果→地址表→写入/冻结/全停。
/// 靶子：SyntheticTarget.x64（已知内存布局；Magic@base+0x1000、Counter@base+0x3000、每秒 +1）。
/// </summary>
public sealed class ScanFreezeIntegrationTests : IDisposable
{
    private const ulong MagicOffset = 0x1000;
    private const ulong CounterOffset = 0x3000;

    private readonly Process _target;
    private readonly ScanEngine _scan = new();
    private readonly AddressBookService _addressBook = new();
    private readonly FreezeService _freeze;
    private readonly List<AddressBookEntry> _entries = [];

    private readonly ulong _baseAddress;

    public ScanFreezeIntegrationTests()
    {
        var exe = FindSyntheticExe();
        // -nocounter：counter 静止在 0，测试可完全控制其值
        _target = Process.Start(new ProcessStartInfo(exe, "-noinput -nocounter")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        // 读取 "Base : 0x..." 行（输出量小，无管道阻塞风险）
        var baseLine = "";
        while (baseLine is not null)
        {
            baseLine = _target.StandardOutput.ReadLine();
            if (baseLine?.StartsWith("Base", StringComparison.OrdinalIgnoreCase) == true)
            {
                break;
            }
        }
        Assert.False(baseLine is null, "SyntheticTarget 未输出 Base 行。");
        var hex = baseLine!.Split(':')[1].Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }
        _baseAddress = ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        _freeze = new FreezeService(id => _entries.FirstOrDefault(e => e.Id == id));

        Thread.Sleep(300); // 等布局写入
    }

    /// <summary>限定扫描范围到合成目标区域附近（避免全内存扫描命中海量候选）。</summary>
    private static (LogicalAddress Start, LogicalAddress End) ScanRange(int pid, ulong baseAddress)
        => (new LogicalAddress(AddressSpace.HostVirtual, $"pid:{pid}", baseAddress - 0x1000),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{pid}", baseAddress + 0x5000));

    [Fact]
    public async Task Scan_To_AddressBook_To_Freeze_ClosedLoop()
    {
        var magicAddress = _baseAddress + MagicOffset;
        var counterAddress = _baseAddress + CounterOffset;
        var (rangeStart, rangeEnd) = ScanRange(_target.Id, _baseAddress);

        // ---- 1. 首次扫描：UInt64 Magic 值（0x1122334455667788 = 十进制 1234605616436508552） ----
        var first = await _scan.BeginFirstScanAsync(
            _target.Id, "synthetic",
            new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.UInt64, "1234605616436508552")),
            rangeStart, rangeEnd, null, default);

        Assert.True(first.CandidateCount >= 1, "Magic 值至少命中 1 个候选。");
        var candidates = await _scan.ReadCandidatesAsync(first.Id, 0, 50, default);
        Assert.Contains(candidates, c => c.Address.Value == magicAddress && c.CurrentValueText == "1234605616436508552");

        // ---- 2. 再次扫描 Unchanged：候选保留 ----
        var second = await _scan.RunNextScanAsync(
            first.Id,
            new ScanCondition(ScanStepKind.NextScan, ScanComparison.Unchanged, null),
            null, default);
        Assert.True(second.CandidateCount >= 1, "Unchanged 再次扫描应保留候选。");

        // ---- 3. 地址表写入：Magic → 0（立即写入 + 读回） ----
        var magicEntry = new AddressBookEntry(
            Guid.NewGuid(),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}", magicAddress),
            "magic", "synthetic", "", null, "集成测试", ScanDataType.UInt64,
            Endianness.LittleEndian, null, true, null, "0");
        _entries.Add(magicEntry);
        await _addressBook.UpsertAsync(magicEntry, default);

        var written = await _addressBook.WriteNowAsync(magicEntry.Id, "0", default);
        Assert.True(written, "立即写入应成功。");
        var readBack = await _addressBook.ReadCurrentAsync(magicEntry, default);
        Assert.Equal("0", readBack);

        // 写入后再次扫描 Equal 0：仍应命中 magic 地址
        var afterWrite = await _scan.RunNextScanAsync(
            first.Id,
            new ScanCondition(ScanStepKind.NextScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.UInt64, "0")),
            null, default);
        Assert.True(afterWrite.CandidateCount >= 1);

        // ---- 4. 冻结 counter（Always 0x12345678；-nocounter 下静止） ----
        var counterEntry = new AddressBookEntry(
            Guid.NewGuid(),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}", counterAddress),
            "counter", "synthetic", "", null, "集成测试", ScanDataType.UInt32,
            Endianness.LittleEndian, null, true, null, "305419896"); // 0x12345678
        _entries.Add(counterEntry);
        await _addressBook.UpsertAsync(counterEntry, default);

        var freezeStatus = await _freeze.StartAsync(
            new FreezeSpec(counterEntry.Id, FreezeConditionKind.Always,
                0x12345678UL, null, null, TimeSpan.FromMilliseconds(100), 5), default);
        Assert.True(freezeStatus.Running);

        await Task.Delay(1300); // 冻结应已生效多次
        var frozenValue = await _addressBook.ReadCurrentAsync(counterEntry, default);
        Assert.Equal("305419896", frozenValue); // 0x12345678

        // ---- 5. 全局停止 → 手动写入不再被冻结覆盖 ----
        await _freeze.StopAllAsync(default);
        await Task.Delay(300); // 等待调度循环退出
        var manualWrite = await _addressBook.WriteNowAsync(counterEntry.Id, "2882400001", default); // 0xABCDEF01
        Assert.True(manualWrite, "停止冻结后手动写入应成功。");
        await Task.Delay(600); // 若冻结未停止，会覆盖回 0x12345678
        var afterStop = await _addressBook.ReadCurrentAsync(counterEntry, default);
        Assert.Equal("2882400001", afterStop); // 未被冻结覆盖
    }

    [Fact]
    public async Task NextScan_Changed_DetectsMutation()
    {
        var counterAddress = _baseAddress + CounterOffset;
        var (rangeStart, rangeEnd) = ScanRange(_target.Id, _baseAddress);

        // 首次扫描 counter 初值 0（UInt32；范围限定在合成区域附近）
        var first = await _scan.BeginFirstScanAsync(
            _target.Id, "counter-probe",
            new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.UInt32, "0")),
            rangeStart, rangeEnd, null, default);
        Assert.True(first.CandidateCount >= 1, "counter 初值 0 应可被扫到。");

        // 手动改写 counter → 再次扫描 Changed：counter 地址应保留
        var counterEntry = new AddressBookEntry(
            Guid.NewGuid(),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}", counterAddress),
            "counter", "synthetic", "", null, "集成测试", ScanDataType.UInt32,
            Endianness.LittleEndian, null, true, null, "42");
        _entries.Add(counterEntry);
        await _addressBook.UpsertAsync(counterEntry, default);
        var ok = await _addressBook.WriteNowAsync(counterEntry.Id, "42", default);
        Assert.True(ok, "counter 改写应成功。");

        var second = await _scan.RunNextScanAsync(
            first.Id,
            new ScanCondition(ScanStepKind.NextScan, ScanComparison.Changed, null),
            null, default);
        var candidates = await _scan.ReadCandidatesAsync(second.Id, 0, 100, default);
        Assert.Contains(candidates, c => c.Address.Value == counterAddress && c.CurrentValueText == "42");
    }

    /// <summary>
    /// 模拟用户 SFC 场景（默认"自动"类型）：8 位数值 21 → 19。
    /// 首次自动扫描（8/16/32 位同时）→ 改写 → 再次自动扫描 → 收敛出真实地址且类型为 8 位。
    /// </summary>
    [Fact]
    public async Task AutoScan_21_to_19_Converges_On_UInt8()
    {
        var targetAddress = _baseAddress + 0x2000;
        var (rangeStart, rangeEnd) = ScanRange(_target.Id, _baseAddress);

        // 1. 在 base+0x2000 写入 8 位值 21（模拟游戏初始数值）
        var hpEntry = new AddressBookEntry(
            Guid.NewGuid(),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}", targetAddress),
            "hp8", "synthetic", "", null, "自动扫描测试", ScanDataType.UInt8,
            Endianness.LittleEndian, null, true, null, "21");
        _entries.Add(hpEntry);
        await _addressBook.UpsertAsync(hpEntry, default);
        Assert.True(await _addressBook.WriteNowAsync(hpEntry.Id, "21", default), "写入 8 位值 21 应成功。");

        // 2. 自动首次扫描 21
        var first = await _scan.BeginFirstScanAsync(
            _target.Id, "auto-21",
            new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.Auto, "21")),
            rangeStart, rangeEnd, null, default);
        Assert.True(first.CandidateCount >= 1, "自动首次扫描应至少命中 1 个候选。");

        // 3. 游戏数值变为 19
        Assert.True(await _addressBook.WriteNowAsync(hpEntry.Id, "19", default), "改写为 19 应成功。");

        // 4. 自动再次扫描 19：8/16/32 位快照各自过滤后收敛
        var second = await _scan.RunNextScanAsync(
            first.Id,
            new ScanCondition(ScanStepKind.NextScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.Auto, "19")),
            null, default);
        Assert.True(second.CandidateCount >= 1, "自动再次扫描应保留候选。");

        // 5. 目标地址必须保留且类型为 8 位、值为 19
        var candidates = await _scan.ReadCandidatesAsync(second.Id, 0, 500, default);
        Assert.Contains(candidates,
            c => c.Address.Value == targetAddress
                 && c.Address.PointerWidthBits == 8
                 && c.CurrentValueText == "19");
    }

    /// <summary>
    /// 回归：候选地址位于 < 1 MiB 小区域/区域末尾时，再次扫描的 1 MiB 块读跨区域边界
    /// 整体失败会导致候选整块丢弃（修复 ReadBlockWithRetry）。目标地址放在 64 MiB 区域末尾 4KB 内。
    /// </summary>
    [Fact]
    public async Task AutoScan_RegionTail_KeepsCandidate()
    {
        var targetAddress = _baseAddress + (64UL * 1024 * 1024) - 0x1000;
        var rangeEnd = new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}",
            _baseAddress + 64UL * 1024 * 1024 + 0x5000);
        var (rangeStart, _) = ScanRange(_target.Id, _baseAddress);

        var tailEntry = new AddressBookEntry(
            Guid.NewGuid(),
            new LogicalAddress(AddressSpace.HostVirtual, $"pid:{_target.Id}", targetAddress),
            "tail8", "synthetic", "", null, "区域边界回归测试", ScanDataType.UInt8,
            Endianness.LittleEndian, null, true, null, "21");
        _entries.Add(tailEntry);
        await _addressBook.UpsertAsync(tailEntry, default);
        Assert.True(await _addressBook.WriteNowAsync(tailEntry.Id, "21", default), "写入 8 位值 21 应成功。");

        var first = await _scan.BeginFirstScanAsync(
            _target.Id, "auto-tail-21",
            new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.Auto, "21")),
            rangeStart, rangeEnd, null, default);
        Assert.True(first.CandidateCount >= 1, "自动首次扫描应至少命中 1 个候选。");

        Assert.True(await _addressBook.WriteNowAsync(tailEntry.Id, "19", default), "改写为 19 应成功。");

        var second = await _scan.RunNextScanAsync(
            first.Id,
            new ScanCondition(ScanStepKind.NextScan, ScanComparison.Equal,
                new ScanValueSpec(ScanDataType.Auto, "19")),
            null, default);
        Assert.True(second.CandidateCount >= 1, "修复后：区域末尾候选在再次扫描中必须保留。");

        var candidates = await _scan.ReadCandidatesAsync(second.Id, 0, 2000, default);
        Assert.Contains(candidates,
            c => c.Address.Value == targetAddress
                 && c.Address.PointerWidthBits == 8
                 && c.CurrentValueText == "19");
    }

    public void Dispose()
    {
        _freeze.Dispose();
        try { _target.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
        _target.Dispose();
    }

    private static string FindSyntheticExe()
    {
        // 测试 bin: tests/Integration/bin/Release/net10.0/
        // 靶子 exe: tests/SyntheticTarget.x64/bin/Release/net10.0/
        var relative = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "SyntheticTarget.x64", "bin", "Release", "net10.0", "SyntheticTarget.x64.exe"));
        if (File.Exists(relative))
        {
            return relative;
        }
        var local = Path.Combine(AppContext.BaseDirectory, "SyntheticTarget.x64.exe");
        if (File.Exists(local))
        {
            return local;
        }
        throw new FileNotFoundException("找不到 SyntheticTarget.x64.exe", relative);
    }
}
