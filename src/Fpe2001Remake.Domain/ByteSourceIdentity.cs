namespace Fpe2001Remake.Domain;

/// <summary>
/// 字节来源身份（规格 §3）。
/// VersionToken：文件源 = 路径规范化+长度+LastWriteUtc+FileId；进程源 = PID+StartTimeUtc+适配器会话。
/// 任何提交前必须复验。
/// </summary>
public sealed record ByteSourceIdentity(
    Guid InstanceId,
    ByteSourceKind Kind,
    string DisplayName,
    ulong? Length,
    string VersionToken);
