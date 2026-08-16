namespace Fpe2001Remake.Contracts;

/// <summary>提交错误类别（规格 §5.2 安全提交 / §6 进程写入）。</summary>
public enum CommitErrorKind
{
    None,
    VersionTokenMismatch,
    TargetProcessChanged,
    PermissionDenied,
    DiskFull,
    ReadBackMismatch,
    ExternalChangeDetected,
    IoError,
    Cancelled,
}

/// <summary>提交选项（规格 §5.2）。</summary>
public sealed record CommitOptions(
    bool VerifyVersionToken = true,
    bool CompareBeforeWrite = true,
    bool ReadBackVerify = true,
    bool CreateBackup = true,
    string? BackupPath = null);

/// <summary>提交结果。失败时保留 RecoveryPath（临时文件/备份）供恢复。</summary>
public sealed record CommitResult(
    bool Success,
    CommitErrorKind ErrorKind,
    string? Message,
    string NewVersionToken,
    string? RecoveryPath)
{
    public static CommitResult Ok(string newVersionToken, string? recoveryPath = null)
        => new(true, CommitErrorKind.None, null, newVersionToken, recoveryPath);

    public static CommitResult Fail(CommitErrorKind kind, string message, string? recoveryPath = null)
        => new(false, kind, message, string.Empty, recoveryPath);
}
