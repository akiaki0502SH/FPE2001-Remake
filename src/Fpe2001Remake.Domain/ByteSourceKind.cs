namespace Fpe2001Remake.Domain;

/// <summary>字节来源种类（规格 §3）。</summary>
public enum ByteSourceKind
{
    /// <summary>本地文件。</summary>
    File,

    /// <summary>进程内存。</summary>
    ProcessMemory,

    /// <summary>只读快照。</summary>
    Snapshot,
}
