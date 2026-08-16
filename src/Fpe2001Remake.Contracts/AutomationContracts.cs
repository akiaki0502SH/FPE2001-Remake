namespace Fpe2001Remake.Contracts;

/// <summary>宏目标绑定（规格 §10：进程身份规则、窗口类/标题规则、适配器会话可选）。</summary>
public sealed record TargetBinding(
    string? ProcessNamePattern = null,
    string? WindowClassPattern = null,
    string? WindowTitlePattern = null,
    string? AdapterSessionId = null);

public enum StepActionKind
{
    KeyDown,
    KeyUp,
    KeyPress,
    MouseLeftClick,
    MouseRightClick,
    MouseLeftDoubleClick,
    MouseRightDoubleClick,
    Delay,
    WaitForeground,
}

/// <summary>宏步骤（规格 §10）。</summary>
public sealed record MacroStep(
    StepActionKind Action,
    string? KeyCode,
    string? MouseButton,
    int DelayMs,
    bool WaitForeground,
    string? Comment);

/// <summary>宏（规格 §10）。</summary>
public sealed record Macro(
    Guid Id,
    string Name,
    TargetBinding Target,
    string? Hotkey,
    IReadOnlyList<MacroStep> Steps,
    int Repeat,
    TimeSpan Timeout,
    bool Enabled);

/// <summary>宏运行状态（规格 §10：倒计时、当前步骤、剩余时间）。</summary>
public sealed record MacroRunState(
    Guid MacroId,
    bool Running,
    int CurrentStepIndex,
    TimeSpan Remaining,
    string? LastError);

/// <summary>
/// 宏自动化服务（规格 §10）。
/// 优先 RegisterHotKey；只有录制/兼容场景才使用低级钩子并显示持续状态；
/// 焦点不匹配、目标退出、超时或 Ctrl+Shift+F12 立即停止。
/// </summary>
public interface IAutomationService
{
    ValueTask<IReadOnlyList<Macro>> ListAsync(CancellationToken ct);

    ValueTask<Macro> SaveAsync(Macro macro, CancellationToken ct);

    ValueTask<bool> DeleteAsync(Guid macroId, CancellationToken ct);

    ValueTask<MacroRunState> RunAsync(Guid macroId, bool countdown, CancellationToken ct);

    ValueTask StopAllAsync(CancellationToken ct);
}

/// <summary>热键服务（优先 RegisterHotKey）。</summary>
public interface IHotkeyService
{
    ValueTask<bool> RegisterAsync(string hotkey, Guid? macroId, CancellationToken ct);

    ValueTask UnregisterAsync(Guid? macroId, CancellationToken ct);
}
