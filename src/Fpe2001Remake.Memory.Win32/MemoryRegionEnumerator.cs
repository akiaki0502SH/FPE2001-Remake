using System.Runtime.CompilerServices;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Memory.Win32;

/// <summary>
/// IMemoryRegionProvider 实现（规格 §8）：VirtualQueryEx 区域规划、保护过滤、checked 推进。
/// 跳过 NOACCESS/GUARD 与 MEM_FREE；返回可读提交区域的逻辑地址区间。
/// </summary>
public sealed class MemoryRegionEnumerator : IMemoryRegionProvider
{
    public async IAsyncEnumerable<ByteRange> EnumerateReadableRegionsAsync(
        int processId, ulong start, ulong end,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        using var handle = ProcessHandle.TryOpen(processId);
        if (handle is null)
        {
            yield break;
        }

        foreach (var region in handle.EnumerateRegions(start, end))
        {
            ct.ThrowIfCancellationRequested();
            if (region.IsReadable)
            {
                yield return new ByteRange(region.BaseAddress, region.RegionSize);
            }
        }
    }
}
