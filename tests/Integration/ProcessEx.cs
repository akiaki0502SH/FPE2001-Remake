using System.Diagnostics;
using System.Globalization;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>集成测试辅助：启动 SyntheticTarget.x64 并读取布局。</summary>
public static class ProcessEx
{
    public static Process StartSyntheticTarget()
    {
        var exe = FindSyntheticExe();
        return Process.Start(new ProcessStartInfo(exe, "-noinput -nocounter")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
    }

    public static ulong ReadBaseAddress(Process target)
    {
        string? baseLine = null;
        while (baseLine is null)
        {
            var line = target.StandardOutput.ReadLine();
            if (line is null)
            {
                throw new InvalidOperationException("SyntheticTarget 输出流已结束，未找到 Base 行。");
            }
            if (line.StartsWith("Base", StringComparison.OrdinalIgnoreCase))
            {
                baseLine = line;
            }
        }
        var hex = baseLine.Split(':')[1].Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }
        return ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static string FindSyntheticExe()
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
