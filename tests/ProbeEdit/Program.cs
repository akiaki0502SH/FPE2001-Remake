using System.Diagnostics;
using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.ByteSources.Process;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Memory.Win32;

// 端到端探针：附加运行中的 snes9x，模拟“编辑器改字节 → 提交写回进程内存”链路。
// 用法：dotnet run --project tests/ProbeEdit -c Release [PID]

var pid = 0;
if (args.Length >= 1)
{
    pid = int.Parse(args[0]);
}
if (pid == 0)
{
    foreach (var p in Process.GetProcessesByName("snes9x"))
    {
        pid = p.Id;
        break;
    }
}
if (pid == 0)
{
    Console.Error.WriteLine("未找到 snes9x 进程。");
    return 2;
}
Console.WriteLine($"目标 PID {pid}");

// 1. 找一个可写区域的可写字节（记录原值）
ulong targetAddr = 0;
byte original = 0;
using (var handle = ProcessHandle.TryOpen(pid))
{
    var region = handle!.EnumerateRegions().FirstOrDefault(r => r.IsWritable && r.RegionSize >= 0x1000);
    if (region is null)
    {
        Console.Error.WriteLine("无可写区域。");
        return 3;
    }
    targetAddr = region.BaseAddress;
    Span<byte> tmp = stackalloc byte[1];
    var n = handle.Read(targetAddr, tmp);
    original = n > 0 ? tmp[0] : (byte)0;
    Console.WriteLine($"可写区域 0x{targetAddr:X16}（原字节 0x{original:X2}）");
}

// 2. 模拟编辑器打开进程视图（4KB 窗口，与 UI 一致）
var baseAddress = targetAddr - (targetAddr % 0x1000);
var source = new ProcessMemoryByteSource(pid, $"pid:{pid}", baseAddress, 0x1000);
using var doc = new HexDocument(source);
var offset = targetAddr - baseAddress;
var before = doc.ReadByte(offset);
Console.WriteLine($"文档偏移 0x{offset:X4} 读回 0x{before:X2}（应等于原字节 0x{original:X2}）");

// 3. 覆盖为 0xAB 并提交（与 ApplyEditAsync 的进程内存路径一致：VerifyVersionToken=false）
var newByte = (byte)(original == 0xAB ? 0xAC : 0xAB);
doc.Overwrite(offset, [newByte]);
var result = await source.CommitAsync(
    doc.BuildChangeSet(),
    new CommitOptions(VerifyVersionToken: false, CreateBackup: false),
    CancellationToken.None);
Console.WriteLine($"提交结果：{(result.Success ? "成功" : $"失败：{result.Message}")}");

// 4. 读回验证
using (var handle = ProcessHandle.TryOpen(pid)!)
{
    Span<byte> tmp = stackalloc byte[1];
    var n = handle.Read(targetAddr, tmp);
    var readBack = n > 0 ? tmp[0] : (byte)0;
    var ok = readBack == newByte;
    Console.WriteLine($"读回 0x{readBack:X2}（期望 0x{newByte:X2}）→ {(ok ? "✅ 字节编辑写入成功" : "❌ 写入失败")}");
    // 还原原值（保持环境干净）
    handle.Write(targetAddr, [original]);
    Console.WriteLine($"已还原为 0x{original:X2}。");
    return ok ? 0 : 5;
}
