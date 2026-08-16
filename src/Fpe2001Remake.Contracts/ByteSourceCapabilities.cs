namespace Fpe2001Remake.Contracts;

/// <summary>字节来源能力位（规格 §4）。</summary>
[Flags]
public enum ByteSourceCapabilities
{
    None = 0,
    Read = 1,
    Overwrite = 2,
    Insert = 4,
    Delete = 8,
    KnownLength = 16,
    AtomicCommit = 32,
    Refresh = 64,
}
