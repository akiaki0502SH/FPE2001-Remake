using System.Diagnostics;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Scan;

// 编码诊断工具：附加到运行中的 snes9x，扫描某个显示值在所有常见编码下的命中数。
// 用法：
//   dotnet run --project tests/ProbeValues -c Release           （自动找 snes9x，值 21）
//   dotnet run --project tests/ProbeValues -c Release -- 19     （值 19）
//   dotnet run --project tests/ProbeValues -c Release -- <pid> <值>
// 游戏里数值显示为 X 时跑一次，记录输出；数值变成 Y 时再跑一次，对比哪个编码两边都大量命中。

var pid = 0;
ulong value = 21;

// 0 参：自动找 snes9x + 值 21；1 参：snes9x + 该值；2 参：PID + 值
if (args.Length >= 2)
{
    pid = int.Parse(args[0]);
    value = ParseValue(args[1]);
}
else if (args.Length == 1)
{
    value = ParseValue(args[0]);
}

if (pid == 0)
{
    pid = FindSnes9xPid();
}
if (pid == 0)
{
    Console.Error.WriteLine("未找到运行中的 snes9x 进程。请先打开游戏，或显式传入 PID。");
    return 2;
}

Console.WriteLine($"目标进程 PID {pid}，诊断显示值 {value}（BCD 编码 = {Bcd(value)} = 十进制 {value} 的 BCD）");
Console.WriteLine("扫描全内存各编码命中数（8/16/32 位 × 十进制 / BCD）：");
Console.WriteLine("------------------------------------------");

var engine = new ScanEngine();
var probes = new (string Label, ScanDataType Type, ulong Probe)[]
{
    ($"8 位 十进制 {value}", ScanDataType.UInt8, value),
    ($"16位 十进制 {value}", ScanDataType.UInt16, value),
    ($"32位 十进制 {value}", ScanDataType.UInt32, value),
    ($"8 位 BCD    {Bcd(value)}", ScanDataType.UInt8, Bcd(value)),
    ($"16位 BCD    {Bcd16(value)}", ScanDataType.UInt16, Bcd16(value)),
    ($"32位 BCD    {Bcd(value)}", ScanDataType.UInt32, Bcd(value)),
};

foreach (var (label, type, probe) in probes)
{
    var sw = Stopwatch.StartNew();
    var session = await engine.BeginFirstScanAsync(pid, "probe-values",
        new ScanCondition(ScanStepKind.FirstScan, ScanComparison.Equal,
            new ScanValueSpec(type, probe.ToString())),
        null, null, null, default);
    sw.Stop();
    Console.WriteLine($"{label,-18} -> 命中 {session.CandidateCount,10:N0}（{sw.Elapsed.TotalSeconds:F1}s）");
}

Console.WriteLine("------------------------------------------");
Console.WriteLine("解读：对比两次（21 和 19）的输出，两边都大量命中且数量接近的编码，");
Console.WriteLine("就是游戏数值在内存中的真实存储形式。");
Console.WriteLine("若 19 的所有编码都是 0：游戏数值可能在两次扫描之间又变化了，或显示值与内存值不一致。");
return 0;

static ulong ParseValue(string s)
{
    s = s.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        return Convert.ToUInt64(s[2..], 16);
    }
    return Convert.ToUInt64(s);
}

/// <summary>BCD 编码：21 → 0x21（十进制 33），19 → 0x19（十进制 25）。</summary>
static ulong Bcd(ulong v)
{
    ulong result = 0;
    var shift = 0;
    while (v > 0)
    {
        result |= (v % 10) << shift;
        v /= 10;
        shift += 4;
    }
    return result;
}

/// <summary>16 位 BCD 编码值：21 → 0x0021 = 33（低字节 BCD，高字节 0）。</summary>
static ulong Bcd16(ulong v) => Bcd(v);

static int FindSnes9xPid()
{
    foreach (var p in Process.GetProcessesByName("snes9x"))
    {
        return p.Id;
    }
    return 0;
}
