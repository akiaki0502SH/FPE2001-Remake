using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Fpe2001Remake.Memory.Win32;

/// <summary>
/// 进程访问句柄：打开进程、区域枚举、读/写。
/// 最小权限（VM_READ|VM_WRITE|VM_OPERATION|QUERY_INFORMATION）；身份复验后使用。
/// </summary>
public sealed class ProcessHandle : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private ProcessHandle(IntPtr handle, ProcessIdentity identity)
    {
        _handle = handle;
        Identity = identity;
    }

    public ProcessIdentity Identity { get; }

    public bool IsOpen => _handle != IntPtr.Zero && !_disposed;

    public static ProcessHandle? TryOpen(ProcessIdentity identity)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_ALL_ACCESS_NEEDED, false, (uint)identity.ProcessId);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }
        return new ProcessHandle(handle, identity);
    }

    public static ProcessHandle? TryOpen(int processId)
    {
        var identity = ProcessIdentity.FromId(processId);
        return identity is null ? null : TryOpen(identity);
    }

    /// <summary>枚举所有内存区域（checked 推进，跳过 MEM_FREE）。</summary>
    public IEnumerable<MemoryRegion> EnumerateRegions(ulong start = 0, ulong? end = null)
    {
        ThrowIfClosed();
        var limit = end ?? 0x0000FFFFFFFFFFFFUL; // 用户态地址上限
        var address = start;
        while (address <= limit)
        {
            if (NativeMethods.VirtualQueryEx(
                    _handle,
                    new IntPtr((long)address),
                    out var mbi,
                    NativeMethods.MEMORY_BASIC_INFORMATION.SizeOf) == 0)
            {
                // 查询失败（越界/句柄失效）：停止枚举
                yield break;
            }

            var region = new MemoryRegion(
                (ulong)mbi.BaseAddress, (ulong)mbi.RegionSize, mbi.Protect, mbi.State, mbi.Type);

            yield return region;

            // checked 推进：RegionSize 为 0 时避免死循环
            if (region.RegionSize == 0)
            {
                yield break;
            }
            try
            {
                address = checked(region.BaseAddress + region.RegionSize);
            }
            catch (OverflowException)
            {
                yield break;
            }
        }
    }

    /// <summary>读取内存；返回实际读到的字节数。</summary>
    public int Read(ulong address, Span<byte> buffer)
    {
        ThrowIfClosed();
        if (buffer.IsEmpty) return 0;
        var bytes = buffer.ToArray();
        var ok = NativeMethods.ReadProcessMemory(
            _handle, new IntPtr((long)address), bytes, (nuint)bytes.Length, out var read);
        if (!ok)
        {
            return 0;
        }
        bytes.AsSpan(0, (int)read).CopyTo(buffer);
        return (int)read;
    }

    /// <summary>写入内存；返回实际写入的字节数。写入前必须由调用方复验 Identity。</summary>
    public int Write(ulong address, ReadOnlySpan<byte> data)
    {
        ThrowIfClosed();
        if (data.IsEmpty) return 0;
        var bytes = data.ToArray();
        var ok = NativeMethods.WriteProcessMemory(
            _handle, new IntPtr((long)address), bytes, (nuint)bytes.Length, out var written);
        return ok ? (int)written : 0;
    }

    /// <summary>写前比较 + 写入 + 读回校验（规格 §6 进程写入三要素）。</summary>
    public bool CompareWriteAndVerify(ulong address, ReadOnlySpan<byte> expectedCurrent, ReadOnlySpan<byte> newValue)
    {
        Span<byte> current = stackalloc byte[Math.Min(256, expectedCurrent.Length)];
        var currentLen = Read(address, current);
        if (currentLen != expectedCurrent.Length ||
            !current[..currentLen].SequenceEqual(expectedCurrent))
        {
            return false; // 当前值与预期不符：不写入
        }

        var written = Write(address, newValue);
        if (written != newValue.Length)
        {
            return false;
        }

        // 读回校验
        Span<byte> readBack = stackalloc byte[Math.Min(256, newValue.Length)];
        var backLen = Read(address, readBack);
        return backLen == newValue.Length && readBack[..backLen].SequenceEqual(newValue);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~ProcessHandle() => Dispose();

    private void ThrowIfClosed()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(ProcessHandle));
        }
    }
}
