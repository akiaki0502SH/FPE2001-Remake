using System.Runtime.InteropServices;
using System.Text;

namespace Fpe2001Remake.SyntheticTarget;

/// <summary>
/// 合成目标进程（P0 出口标准：供扫描/读取测试的已知内存布局）。
/// 布局（相对基址偏移）：
///   0x1000  Magic   ：ulong 0x1122334455667788
///   0x1010  Magic2  ：ulong 0xDEADBEEFCAFEBABE
///   0x2000  Pattern ：ASCII "FPE2001-REMAKE-SYNTHETIC-TARGET"（重复 3 次）
///   0x3000  Counter ：int32 每秒 +1（-nocounter 关闭）
///   0x4000  Buffer  ：64 KiB 递增字节 0x00..0xFF 循环
/// 进程常驻，直到收到按键 / Ctrl+C / stdin EOF。
/// </summary>
internal static class Program
{
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint PAGE_READWRITE = 0x04;
    private const int AllocationSize = 64 * 1024 * 1024; // 64 MiB

    public const ulong MagicOffset = 0x1000;
    public const ulong Magic2Offset = 0x1010;
    public const ulong PatternOffset = 0x2000;
    public const ulong CounterOffset = 0x3000;
    public const ulong BufferOffset = 0x4000;
    public const int BufferLength = 64 * 1024;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(nint lpAddress, nuint dwSize, uint dwFreeType);

    private static int Main(string[] args)
    {
        var noCounter = args.Contains("-nocounter", StringComparer.OrdinalIgnoreCase);
        var noInput = args.Contains("-noinput", StringComparer.OrdinalIgnoreCase);

        nint basePtr = VirtualAlloc(0, (nuint)AllocationSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (basePtr == 0)
        {
            Console.Error.WriteLine($"VirtualAlloc 失败：Win32 错误 {Marshal.GetLastWin32Error()}");
            return 2;
        }

        try
        {
            // 布局写入
            WriteUInt64(basePtr, MagicOffset, 0x1122334455667788UL);
            WriteUInt64(basePtr, Magic2Offset, 0xDEADBEEFCAFEBABEUL);

            var pattern = Encoding.ASCII.GetBytes("FPE2001-REMAKE-SYNTHETIC-TARGET");
            for (var i = 0; i < 3; i++)
            {
                Marshal.Copy(pattern, 0, basePtr + (nint)(PatternOffset + (ulong)(i * pattern.Length)), pattern.Length);
            }

            WriteInt32(basePtr, CounterOffset, 0);

            var buffer = new byte[BufferLength];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)(i & 0xFF);
            }
            Marshal.Copy(buffer, 0, basePtr + (nint)BufferOffset, buffer.Length);

            Console.WriteLine("=== FPE2001-Remake SyntheticTarget.x64 ===");
            Console.WriteLine($"PID        : {Environment.ProcessId}");
            Console.WriteLine($"Base       : 0x{(ulong)basePtr:X16}");
            Console.WriteLine($"Size       : {AllocationSize / (1024 * 1024)} MiB");
            Console.WriteLine($"Magic      : {MagicOffset:X4} -> 0x1122334455667788");
            Console.WriteLine($"Magic2     : {Magic2Offset:X4} -> 0xDEADBEEFCAFEBABE");
            Console.WriteLine($"Pattern    : {PatternOffset:X4} -> {pattern.Length * 3} bytes");
            Console.WriteLine($"Counter    : {CounterOffset:X4} -> int32 {(noCounter ? "static" : "每秒 +1")}");
            Console.WriteLine($"Buffer     : {BufferOffset:X4} -> {BufferLength} bytes 0x00..0xFF");
            Console.WriteLine("------------------------------------------");
            Console.WriteLine("按任意键或 Ctrl+C 退出。");

            var exit = new ManualResetEventSlim(false);
            if (!noInput)
            {
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
                _ = Task.Run(() => { Console.ReadKey(true); exit.Set(); });
                _ = Task.Run(() => { Console.In.ReadToEnd(); exit.Set(); });
            }

            if (!noCounter)
            {
                _ = Task.Run(async () =>
                {
                    var value = 0;
                    while (!exit.IsSet)
                    {
                        await Task.Delay(1000);
                        value++;
                        WriteInt32(basePtr, CounterOffset, value);
                    }
                });
            }

            exit.Wait();
            Console.WriteLine("退出。");
            return 0;
        }
        finally
        {
            VirtualFree(basePtr, 0, 0x8000 /* MEM_RELEASE */);
        }
    }

    private static unsafe void WriteUInt64(nint basePtr, ulong offset, ulong value)
        => *(ulong*)(basePtr + (nint)offset) = value;

    private static unsafe void WriteInt32(nint basePtr, ulong offset, int value)
        => *(int*)(basePtr + (nint)offset) = value;
}
