using System.Diagnostics;

namespace Fpe2001Remake.Memory.Win32;

/// <summary>
/// 进程身份（规格 §3 VersionToken：PID + StartTimeUtc + 适配器会话）。
/// 任何写入前必须复验；目标重启后所有提交拒绝。
/// </summary>
public sealed record ProcessIdentity(int ProcessId, string ProcessName, DateTimeOffset StartTimeUtc)
{
    public string VersionToken => $"pid:{ProcessId};start:{StartTimeUtc:o}";

    public static ProcessIdentity? FromId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessIdentity(processId, process.ProcessName, process.StartTime.ToUniversalTime());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>复验：进程仍存在且启动时间一致。</summary>
    public bool Verify()
    {
        var current = FromId(ProcessId);
        return current is not null && current.StartTimeUtc == StartTimeUtc;
    }
}
