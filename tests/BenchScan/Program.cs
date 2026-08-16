using System.Diagnostics;
using System.Globalization;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Scan;

// 扫描性能基准：SyntheticTarget（64 MiB 可读区）全内存自动模式（8/16/32 位）
// 用法：dotnet run --project tests/BenchScan -c Release

var exe = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..",
    "SyntheticTarget.x64", "bin", "Release", "net10.0", "SyntheticTarget.x64.exe"));
if (!File.Exists(exe))
{
    Console.Error.WriteLine($"找不到 SyntheticTarget.x64.exe：{exe}");
    return 2;
}

using var target = Process.Start(new ProcessStartInfo(exe, "-noinput -nocounter")
{
    CreateNoWindow = true,
    UseShellExecute = false,
    RedirectStandardOutput = true,
})!;

var baseLine = "";
while (baseLine is not null)
{
    baseLine = target.StandardOutput.ReadLine();
    if (baseLine?.StartsWith("Base", StringComparison.OrdinalIgnoreCase) == true) break;
}
if (baseLine is null)
{
    Console.Error.WriteLine("SyntheticTarget 未输出 Base 行。");
    return 3;
}
Thread.Sleep(300);

var engine = new ScanEngine();
var pid = target.Id;

// ---- 基准 1：首次自动扫描 21（全内存，8/16/32 位同时） ----
var sw = Stopwatch.StartNew();
var first = await engine.BeginFirstScanAsync(pid, "bench-auto-21",
    new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
        new ScanValueSpec(ScanDataType.Auto, "21")),
    null, null, null, default);
sw.Stop();
Console.WriteLine($"[首次 自动] 全内存扫 21：{sw.Elapsed.TotalSeconds:F2}s，候选 {first.CandidateCount:N0}");

var snapshotDir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "snapshots");
foreach (var f in Directory.GetFiles(snapshotDir, first.Id.ToString("N") + "*"))
{
    Console.WriteLine($"  快照 {Path.GetFileName(f)}：{new FileInfo(f).Length / (1024.0 * 1024.0):F1} MB");
}

// ---- 基准 2：再次扫描 19（全内存过滤，值未变） ----
var sw2 = Stopwatch.StartNew();
var second = await engine.RunNextScanAsync(first.Id,
    new ScanCondition(ScanStepKind.NextScan, ScanComparison.Equal,
        new ScanValueSpec(ScanDataType.Auto, "19")),
    null, default);
sw2.Stop();
Console.WriteLine($"[再次 自动] 全内存过滤 19：{sw2.Elapsed.TotalSeconds:F2}s，候选 {second.CandidateCount:N0}");

// ---- 基准 3：单类型 32 位对比（旧默认行为） ----
var sw3 = Stopwatch.StartNew();
var third = await engine.BeginFirstScanAsync(pid, "bench-32-21",
    new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
        new ScanValueSpec(ScanDataType.UInt32, "21")),
    null, null, null, default);
sw3.Stop();
Console.WriteLine($"[首次 32位] 全内存扫 21：{sw3.Elapsed.TotalSeconds:F2}s，候选 {third.CandidateCount:N0}");

Console.WriteLine("基准完成。");
target.Kill(entireProcessTree: true);
return 0;
