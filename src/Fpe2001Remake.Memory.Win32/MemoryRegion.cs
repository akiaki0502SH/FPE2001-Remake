namespace Fpe2001Remake.Memory.Win32;

/// <summary>内存区域信息（VirtualQueryEx 结果）。</summary>
public sealed record MemoryRegion(
    ulong BaseAddress,
    ulong RegionSize,
    uint Protect,
    uint State,
    uint Type)
{
    public bool IsCommit => State == NativeMethods.MEM_COMMIT;

    /// <summary>可读提交区域：非 NOACCESS/GUARD，且含读/写/执行读权限之一。</summary>
    public bool IsReadable =>
        IsCommit &&
        (Protect & (NativeMethods.PAGE_NOACCESS | NativeMethods.PAGE_GUARD)) == 0 &&
        (Protect & (NativeMethods.PAGE_READONLY | NativeMethods.PAGE_READWRITE |
                    NativeMethods.PAGE_WRITECOPY | NativeMethods.PAGE_EXECUTE_READ |
                    NativeMethods.PAGE_EXECUTE_READWRITE | NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;

    /// <summary>可写（含写权限）。</summary>
    public bool IsWritable =>
        IsCommit &&
        (Protect & (NativeMethods.PAGE_READWRITE | NativeMethods.PAGE_WRITECOPY |
                    NativeMethods.PAGE_EXECUTE_READWRITE | NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;
}
