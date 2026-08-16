using System.Diagnostics;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Memory.Win32;
using Fpe2001Remake.Scan;

// snes9x 真实进程探针：区域统计 → 自动扫描 21 → 手动改写候选为 19 → 再次扫描 19
// 用法：dotnet run --project tests/ProbeSnes9x -c Release

var snes9xPath = @"G:\Emu\SFC\Snes9x\snes9x.exe";
if (!File.Exists(snes9xPath))
{
    Console.Error.WriteLine($"找不到 snes9x：{snes9xPath}");
    return 2;
}

using var proc = Process.Start(new ProcessStartInfo(snes9xPath)
{
    WorkingDirectory = Path.GetDirectoryName(snes9xPath),
})!;
Thread.Sleep(4000); // 等 UI 初始化

var pid = proc.Id;
Console.WriteLine($"snes9x PID: {pid}（架构 {(Environment.Is64BitProcess ? "?" : "?")}）");

// ---- 1. 区域统计 ----
using (var handle = ProcessHandle.TryOpen(pid))
{
    if (handle is null)
    {
        Console.Error.WriteLine("无法打开 snes9x 进程。");
        return 3;
    }
    var regions = handle.EnumerateRegions().ToList();
    var readable = regions.Where(r => r.IsReadable).ToList();
    var totalBytes = readable.Aggregate(0UL, (a, r) => a + r.RegionSize);
    var small = readable.Where(r => r.RegionSize < 1024UL * 1024).ToList();
    Console.WriteLine($"区域总数 {regions.Count}，可读 {readable.Count}，可读总字节 {totalBytes / (1024.0 * 1024):F1} MB");
    Console.WriteLine($"<1MiB 可读区域 {small.Count} 个（共 {small.Aggregate(0UL, (a, r) => a + r.RegionSize) / (1024.0 * 1024):F1} MB）");
    var sizeBins = readable.GroupBy(r => r.RegionSize switch
    {
        < 4096 => "<4K",
        < 65536 => "4K-64K",
        < 1024 * 1024 => "64K-1M",
        _ => ">=1M",
    }).Select(g => $"{g.Key}:{g.Count()}").ToArray();
    Console.WriteLine($"区域大小分布：{string.Join(" ", sizeBins)}");
}

// ---- 2. 自动首次扫描 21 ----
var engine = new ScanEngine();
var sw = Stopwatch.StartNew();
var first = await engine.BeginFirstScanAsync(pid, "probe-21",
    new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
        new ScanValueSpec(ScanDataType.Auto, "21")),
    null, null, null, default);
sw.Stop();
Console.WriteLine($"[首次 自动] 全内存扫 21：{sw.Elapsed.TotalSeconds:F2}s，候选 {first.CandidateCount:N0}");

var candidates = await engine.ReadCandidatesAsync(first.Id, 0, 50, default);
Console.WriteLine($"候选前 50 条（类型/值/地址）：");
foreach (var c in candidates.Take(15))
{
    Console.WriteLine($"  0x{c.Address.Value:X12} 宽{c.Address.PointerWidthBits} 值={c.CurrentValueText}");
}
if (candidates.Count == 0)
{
    Console.WriteLine("无候选，无法继续改写验证。");
    proc.Kill(entireProcessTree: true);
    return 4;
}

// ---- 3. 手动把「可写区域内」的候选改写为 19（模拟游戏数值 21→19） ----
List<(ulong Start, ulong End)> writable;
using (var h2 = ProcessHandle.TryOpen(pid))
{
    writable = h2!.EnumerateRegions()
        .Where(r => r.IsWritable)
        .Select(r => (r.BaseAddress, r.BaseAddress + r.RegionSize))
        .ToList();
}
Console.WriteLine($"可写区域 {writable.Count} 个");

var target = candidates.FirstOrDefault(c => writable.Any(w => c.Address.Value >= w.Start && c.Address.Value < w.End));
if (target is null)
{
    Console.WriteLine("前 50 条候选无落在可写区域的，扩大候选范围重找…");
    var all = await engine.ReadCandidatesAsync(first.Id, 0, 5000, default);
    target = all.FirstOrDefault(c => writable.Any(w => c.Address.Value >= w.Start && c.Address.Value < w.End));
}
if (target is null)
{
    Console.WriteLine("仍无可写候选（无法验证写入路径）。");
}
else
{
    Console.WriteLine($"改写目标：0x{target.Address.Value:X12} 宽{target.Address.PointerWidthBits} 值={target.CurrentValueText}");
    var widthBytes = target.Address.PointerWidthBits / 8;
    var newValue = new byte[widthBytes];
    for (var i = 0; i < widthBytes; i++)
    {
        newValue[i] = (byte)((19UL >> (8 * i)) & 0xFF);
    }
    using var h = ProcessHandle.TryOpen(pid);
    var written = h!.Write(target.Address.Value, newValue);
    Console.WriteLine($"写入 19：{written}/{widthBytes} 字节");
    if (written != widthBytes)
    {
        Console.WriteLine("⚠️ 写入失败，目标区域不可写？");
    }
    var readBack = h.Read(target.Address.Value, newValue.AsSpan(0, widthBytes));
    Console.WriteLine($"读回验证：{readBack} 字节 = {string.Join(" ", newValue.Take(widthBytes).Select(b => b.ToString("X2")))}");
}

// ---- 4. 自动再次扫描 19 ----
var sw2 = Stopwatch.StartNew();
var second = await engine.RunNextScanAsync(first.Id,
    new ScanCondition(ScanStepKind.NextScan, ScanComparison.Equal,
        new ScanValueSpec(ScanDataType.Auto, "19")),
    null, default);
sw2.Stop();
Console.WriteLine($"[再次 自动] 扫 19：{sw2.Elapsed.TotalSeconds:F2}s，候选 {second.CandidateCount:N0}");

var c2 = await engine.ReadCandidatesAsync(second.Id, 0, 100, default);
Console.WriteLine($"再次候选 {c2.Count} 条：");
foreach (var c in c2.Take(10))
{
    Console.WriteLine($"  0x{c.Address.Value:X12} 宽{c.Address.PointerWidthBits} 值={c.CurrentValueText}");
}
var kept = target is not null && c2.Any(x => x.Address.Value == target.Address.Value);
Console.WriteLine(kept ? "✅ 目标地址在再次扫描中保留" : "❌ 目标地址丢失！");
Console.WriteLine(second.CandidateCount == 0 ? "⚠️ 再次扫描 0 候选（复现用户场景）" : "✅ 再次扫描有候选");

proc.Kill(entireProcessTree: true);
return kept ? 0 : 5;
