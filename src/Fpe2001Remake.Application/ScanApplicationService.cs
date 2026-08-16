using System.Diagnostics;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Scan;

namespace Fpe2001Remake.Application;

/// <summary>
/// 扫描应用服务（M01）：目标进程列表与活动会话。
/// 列出所有用户态进程（含无窗口控制台目标）；原版 FPE 可附加任意进程。
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
        var targets = Process.GetProcesses()
            .Where(p =>
            {
                try
                {
                    return !p.HasExited && p.Id != self;
                }
                catch
                {
                    return false;
                }
            })
            .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Id, p.ProcessName))
            .ToList();
        return ValueTask.FromResult<IReadOnlyList<(int ProcessId, string ProcessName)>>(targets);
    }

    public ValueTask<ScanSession?> GetActiveSessionAsync(CancellationToken ct)
        => ValueTask.FromResult<ScanSession?>(null);
}
