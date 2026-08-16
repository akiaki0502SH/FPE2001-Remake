using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Automation;

/// <summary>
/// 宏自动化服务（规格 §10）：
/// 持久化（%APPDATA%/FPE2001-Remake/macros.json）、执行（SendInput 步骤序列）、
/// 目标窗口聚焦（SetForegroundWindow）、倒计时、急停（StopAllAsync）。
/// </summary>
public sealed class AutomationService : IAutomationService
{
    private const string StoreFileName = "macros.json";

    private readonly string _storePath;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Macro> _macros = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _running = [];
    private readonly HotkeyService? _hotkeys;

    public AutomationService(HotkeyService? hotkeys = null, string? storeDirectory = null)
    {
        _hotkeys = hotkeys;
        var dir = storeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FPE2001-Remake");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, StoreFileName);
        Load();
    }

    public ValueTask<IReadOnlyList<Macro>> ListAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<Macro>>(_macros.Values.OrderBy(m => m.Name).ToList());
        }
    }

    public ValueTask<Macro> SaveAsync(Macro macro, CancellationToken ct)
    {
        lock (_gate)
        {
            _macros[macro.Id] = macro;
            Persist();
            return ValueTask.FromResult(macro);
        }
    }

    public ValueTask<bool> DeleteAsync(Guid macroId, CancellationToken ct)
    {
        lock (_gate)
        {
            StopLocked(macroId);
            _hotkeys?.UnregisterAsync(macroId, ct).GetAwaiter().GetResult();
            var removed = _macros.Remove(macroId);
            if (removed) Persist();
            return ValueTask.FromResult(removed);
        }
    }

    public async ValueTask<MacroRunState> RunAsync(Guid macroId, bool countdown, CancellationToken ct)
    {
        Macro? macro;
        lock (_gate)
        {
            _macros.TryGetValue(macroId, out macro);
            if (macro is null)
            {
                return new MacroRunState(macroId, false, 0, TimeSpan.Zero, "宏不存在。");
            }
        }

        StopLocked(macroId);
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            _running[macroId] = runCts;
        }

        var started = DateTimeOffset.UtcNow;
        var runTask = Task.Run(() => ExecuteAsync(macro, runCts.Token), CancellationToken.None);

        try
        {
            await runTask;
            return new MacroRunState(macroId, false, 0, TimeSpan.Zero, null);
        }
        catch (OperationCanceledException)
        {
            return new MacroRunState(macroId, false, 0, TimeSpan.Zero, "已停止。");
        }
        catch (Exception ex)
        {
            return new MacroRunState(macroId, false, 0, TimeSpan.Zero, ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(macroId);
            }
        }
    }

    public ValueTask StopAllAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            foreach (var id in _running.Keys.ToList())
            {
                StopLocked(id);
            }
        }
        return ValueTask.CompletedTask;
    }

    // ---------- 内部 ----------

    private void StopLocked(Guid macroId)
    {
        if (_running.Remove(macroId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task ExecuteAsync(Macro macro, CancellationToken ct)
    {
        var target = macro.Target;
        for (var repeat = 0; repeat < Math.Max(1, macro.Repeat); repeat++)
        {
            foreach (var step in macro.Steps)
            {
                ct.ThrowIfCancellationRequested();

                switch (step.Action)
                {
                    case StepActionKind.Delay:
                        await Task.Delay(Math.Max(0, step.DelayMs), ct);
                        break;
                    case StepActionKind.WaitForeground:
                        if (!TryFocusTarget(target))
                        {
                            throw new InvalidOperationException("未找到目标窗口。");
                        }
                        await Task.Delay(step.DelayMs, ct);
                        break;
                    case StepActionKind.KeyDown:
                    case StepActionKind.KeyUp:
                    case StepActionKind.KeyPress:
                        if (string.IsNullOrEmpty(step.KeyCode))
                        {
                            throw new InvalidOperationException("按键步骤缺少键码。");
                        }
                        if (!NativeInput.TryParseHotkey(step.KeyCode, out _, out var vk))
                        {
                            throw new InvalidOperationException($"无效的键码：{step.KeyCode}");
                        }
                        if (step.Action == StepActionKind.KeyUp)
                        {
                            NativeInput.SendKey((ushort)vk, down: false);
                        }
                        else if (step.Action == StepActionKind.KeyDown)
                        {
                            NativeInput.SendKey((ushort)vk, down: true);
                        }
                        else
                        {
                            NativeInput.SendKey((ushort)vk, down: true);
                            NativeInput.SendKey((ushort)vk, down: false);
                        }
                        break;
                    case StepActionKind.MouseLeftClick:
                    case StepActionKind.MouseRightClick:
                    case StepActionKind.MouseLeftDoubleClick:
                    case StepActionKind.MouseRightDoubleClick:
                        var right = step.Action is StepActionKind.MouseRightClick or StepActionKind.MouseRightDoubleClick;
                        var clicks = step.Action is StepActionKind.MouseLeftDoubleClick or StepActionKind.MouseRightDoubleClick ? 2 : 1;
                        for (var c = 0; c < clicks; c++)
                        {
                            NativeInput.SendMouseClick(right, down: true);
                            NativeInput.SendMouseClick(right, down: false);
                        }
                        break;
                }

                if (step.DelayMs > 0 && step.Action is not StepActionKind.Delay and not StepActionKind.WaitForeground)
                {
                    await Task.Delay(step.DelayMs, ct);
                }
            }
        }
    }

    private static bool TryFocusTarget(TargetBinding target)
    {
        if (string.IsNullOrEmpty(target.WindowTitlePattern) &&
            string.IsNullOrEmpty(target.WindowClassPattern) &&
            string.IsNullOrEmpty(target.ProcessNamePattern))
        {
            return true;
        }
        return FindAndFocus(target);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    private static bool FindAndFocus(TargetBinding target)
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (target.ProcessNamePattern is { Length: > 0 } pn &&
                    !p.ProcessName.Contains(pn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }
                if (target.WindowTitlePattern is { Length: > 0 } wt &&
                    !p.MainWindowTitle.Contains(wt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                SetForegroundWindow(p.MainWindowHandle);
                return true;
            }
            catch
            {
                // 进程已退出等：跳过
            }
        }
        return false;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = File.ReadAllText(_storePath);
            var list = JsonSerializer.Deserialize<List<Macro>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list is null) return;
            foreach (var m in list)
            {
                _macros[m.Id] = m;
            }
        }
        catch
        {
            // 损坏的存储：忽略，从空开始
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_macros.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }
        catch
        {
            // 持久化失败不致命
        }
    }
}
