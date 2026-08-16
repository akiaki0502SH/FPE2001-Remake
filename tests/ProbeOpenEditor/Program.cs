using System.Diagnostics;
using Fpe2001Remake.Memory.Win32;
using Fpe2001Remake.UI.ViewModels.Modules;

// 定位链路探针：附加 snes9x，写入已知字节 0x13，模拟“从扫描结果打开编辑器”，
// 验证 OpenProcessAtCoreAsync 后 SelectedRow/SelectedByteOffset/EditValue 都指向目标字节。
// 用法：dotnet run --project tests/ProbeOpenEditor -c Release [PID]

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

// 1. 找一个可写区域地址并写入 0x13（模拟搜索结果指向的字节）
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
    targetAddr = region.BaseAddress + 0x100; // 窗口中部，避开区域起点
    Span<byte> tmp = stackalloc byte[1];
    original = handle.Read(targetAddr, tmp) > 0 ? tmp[0] : (byte)0;
    handle.Write(targetAddr, [(byte)0x13]); // 写入 19 的十六进制
}
Console.WriteLine($"目标地址 0x{targetAddr:X12}（已写入 0x13，原值 0x{original:X2}）");

// 2. 模拟 MainViewModel 回调：页对齐打开 4KB 窗口
const ulong window = 0x1000;
var baseAddress = targetAddr - (targetAddr % window);
var vm = new HexEditorViewModel();
await vm.OpenProcessAtCoreAsync(pid, baseAddress, window, targetAddr);

// 3. 断言定位状态
var docOffset = targetAddr - baseAddress;
var expectedRow = (int)(docOffset / 16);
var expectedColumn = (int)(docOffset % 16);

Console.WriteLine($"视图基址 0x{baseAddress:X12}，文档偏移 0x{docOffset:X3}，期望行 {expectedRow}，列 {expectedColumn}");
Console.WriteLine($"Rows.Count = {vm.Rows?.Count}");
Console.WriteLine($"SelectedRow.Offset = 0x{vm.SelectedRow?.Offset:X3}（期望 0x{docOffset - (ulong)expectedColumn:X3}）");
Console.WriteLine($"SelectedByteOffset = 0x{vm.SelectedByteOffset:X3}（期望 0x{docOffset:X3}）");
Console.WriteLine($"FocusByteColumn = {vm.FocusByteColumn}（期望 {expectedColumn}）");
Console.WriteLine($"EditValue = {vm.EditValue}（期望 13）");
Console.WriteLine($"PendingFocusOffset = 0x{vm.PendingFocusOffset:X3}");

var ok = vm.SelectedRow?.Offset == docOffset - (ulong)expectedColumn
         && vm.SelectedByteOffset == docOffset
         && vm.FocusByteColumn == expectedColumn
         && vm.EditValue == "13";
Console.WriteLine(ok ? "✅ 定位链路正确：行/字节/列/预填值全部命中" : "❌ 定位链路仍有偏差");

// 还原原值
using (var handle = ProcessHandle.TryOpen(pid))
{
    handle!.Write(targetAddr, [original]);
}
return ok ? 0 : 5;
