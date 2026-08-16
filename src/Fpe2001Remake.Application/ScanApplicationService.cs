using System.Diagnostics;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Scan;

namespace Fpe2001Remake.Application;

/// <summary>
/// 扫描应用服务（M01）：目标进程列表与活动会话。
/// 列出交互式用户目标（含无窗口控制台目标），隐藏 Windows 服务和系统进程。
/// 原版 FPE 可附加任意进程；过滤只作用于默认列表，十六进制页仍支持按 PID 打开。
/// </summary>
public sealed class ScanApplicationService : IScanApplicationService
{
    private readonly ScanEngine _engine;

    public ScanApplicationService(ScanEngine engine)
    {
        _engine = engine;
    }

    public ValueTask<IReadOnlyList<(int ProcessId, string ProcessName)>> ListTargetsAsync(CancellationToken ct)
    {
        var self = Environment.ProcessId;
        var targets = new List<(int ProcessId, string ProcessName)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var candidate = new ProcessTargetDescriptor(
                        process.Id,
                        process.ProcessName,
                        process.SessionId,
                        TryGetExecutablePath(process));

                    if (ProcessTargetFilter.IsEligible(candidate, self))
                    {
                        targets.Add((candidate.ProcessId, candidate.ProcessName));
                    }
                }
                catch (InvalidOperationException)
                {
                    // 进程在枚举期间退出，跳过本次刷新。
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 受保护进程可能拒绝读取属性，跳过本次刷新。
                }
            }
        }

        targets.Sort(static (left, right) =>
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(left.ProcessName, right.ProcessName);
            return byName != 0 ? byName : left.ProcessId.CompareTo(right.ProcessId);
        });
        return ValueTask.FromResult<IReadOnlyList<(int ProcessId, string ProcessName)>>(targets);
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public ValueTask<ScanSession?> GetActiveSessionAsync(CancellationToken ct)
        => ValueTask.FromResult<ScanSession?>(null);
}
