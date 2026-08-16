using System.Collections.Generic;

namespace Fpe2001Remake.Application;

/// <summary>供扫描页使用的进程候选信息。</summary>
public readonly record struct ProcessTargetDescriptor(
    int ProcessId,
    string ProcessName,
    int SessionId,
    string? ExecutablePath);

/// <summary>
/// Windows 目标进程过滤规则。
/// 只隐藏系统/服务进程，不限制用户选择自定义程序、游戏或模拟器。
/// </summary>
public static class ProcessTargetFilter
{
    private static readonly HashSet<string> WindowsSystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg",
        "csrss",
        "dwm",
        "fontdrvhost",
        "idle",
        "lsass",
        "lsm",
        "memory compression",
        "msdtc",
        "ngentask",
        "registry",
        "services",
        "smss",
        "spoolsv",
        "svchost",
        "system",
        "system idle process",
        "trustedinstaller",
        "wininit",
        "winlogon",
        "wudfhost"
    };

    public static bool IsEligible(
        ProcessTargetDescriptor candidate,
        int currentProcessId,
        string? windowsDirectory = null)
    {
        if (candidate.ProcessId <= 0 || candidate.ProcessId == currentProcessId)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.ProcessName))
        {
            return false;
        }

        if (candidate.SessionId == 0)
        {
            // Session 0 is reserved for services and non-interactive system processes.
            return false;
        }

        var processName = NormalizeProcessName(candidate.ProcessName);
        if (WindowsSystemProcessNames.Contains(processName))
        {
            return false;
        }

        return !IsUnderWindowsDirectory(candidate.ExecutablePath, windowsDirectory);
    }

    private static string NormalizeProcessName(string processName)
    {
        var fileName = Path.GetFileName(processName);
        return Path.GetFileNameWithoutExtension(fileName).Trim();
    }

    private static bool IsUnderWindowsDirectory(string? executablePath, string? windowsDirectory)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            // Access to protected processes can fail; name/session rules still apply.
            return false;
        }

        var root = windowsDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(executablePath);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
