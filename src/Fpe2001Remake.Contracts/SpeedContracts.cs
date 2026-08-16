namespace Fpe2001Remake.Contracts;

/// <summary>速度值域（规格 §11：值域与步进来自适配器）。</summary>
public sealed record SpeedRange(
    double Min,
    double Max,
    double Step,
    IReadOnlyList<double> RecommendedMultipliers);

/// <summary>速度状态（规格 §11）。</summary>
public sealed record SpeedState(bool Enabled, double Multiplier);

/// <summary>目标/适配器无速度能力（规格 §11：不尝试通用 DLL 钩子）。</summary>
public sealed class SpeedUnsupportedException : NotSupportedException
{
    public SpeedUnsupportedException(string message) : base(message) { }
}

/// <summary>
/// 速度控制能力（规格 §11）。目标暂停、快进和速度倍率是独立能力，不互相推断。
/// UI 显示 0.25x/0.5x/1x/2x 等推荐刻度；离开页面不恢复。
/// </summary>
public interface ISpeedControlCapability
{
    SpeedRange Range { get; }

    ValueTask<SpeedState> GetAsync(CancellationToken ct);

    ValueTask<SpeedState> SetEnabledAsync(bool enabled, CancellationToken ct);

    ValueTask<SpeedState> SetMultiplierAsync(double multiplier, CancellationToken ct);
}
