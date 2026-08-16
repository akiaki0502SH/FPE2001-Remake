namespace Fpe2001Remake.Contracts;

/// <summary>适配器能力位（规格 §11）。</summary>
[Flags]
public enum AdapterCapabilities
{
    None = 0,
    Read = 1,
    Write = 2,
    Domains = 4,
    GuestVirtualTranslation = 8,
    PauseResume = 16,
    FrameSync = 32,
    SpeedControl = 64,
    SaveRam = 128,
    Diagnostics = 256,
}

/// <summary>
/// IPC 帧（规格 §12，沿用 v1 帧结构）：
/// protocolVersion、messageId、correlationId、deadlineUtc、nonce。
/// 地址/偏移在 JSON 载荷中一律序列化为 0x+16 位字符串。
/// </summary>
public sealed record AdapterMessage(
    int ProtocolVersion,
    string MessageId,
    string CorrelationId,
    DateTimeOffset DeadlineUtc,
    string Nonce,
    string Method,
    string? PayloadJson);

/// <summary>协议常量（规格 §12）。</summary>
public static class AdapterProtocol
{
    public const int ProtocolVersion = 1;
    public const int MaxFrameBytes = 4 * 1024 * 1024;
}

/// <summary>
/// 模拟器适配器（规格 §11/§12）。
/// 每个解析地址包含 ResolutionProof；进程重启或目标版本变化后旧证明失效。
/// 一实例一适配器；崩溃不拖垮 UI。
/// </summary>
public interface IEmulatorAdapter : IAsyncDisposable
{
    string AdapterId { get; }

    string EmulatorName { get; }

    /// <summary>目标精确版本标识（版本 + SHA-256 依据）。</summary>
    string VersionToken { get; }

    AdapterCapabilities Capabilities { get; }

    ValueTask<AdapterMessage> InvokeAsync(AdapterMessage request, CancellationToken ct);
}
