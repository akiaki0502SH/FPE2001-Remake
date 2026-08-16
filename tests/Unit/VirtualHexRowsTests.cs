using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.UI.ViewModels.Modules;
using Xunit;

namespace Fpe2001Remake.UnitTests;

/// <summary>
/// 虚拟行集合缓存一致性回归测试：
/// 同一索引必须返回同一实例，否则 DataGrid.ScrollIntoView/选中引用不匹配，
/// “从扫描结果打开编辑器定位”会失败（历史 bug：索引器每次 new 新实例）。
/// </summary>
public sealed class VirtualHexRowsTests
{
    private static VirtualHexRows CreateRows(int length = 4096)
    {
        var doc = new HexDocument(new MemoryByteSource(new byte[length]));
        return new VirtualHexRows(doc);
    }

    [Fact]
    public void SameIndex_ReturnsSameInstance()
    {
        var rows = CreateRows();
        var a = rows[10];
        var b = rows[10];
        Assert.Same(a, b); // 缓存命中：引用一致
    }

    [Fact]
    public void DifferentIndex_ReturnsDifferentInstances()
    {
        var rows = CreateRows();
        Assert.NotSame(rows[10], rows[11]);
    }

    [Fact]
    public void RepeatedAccess_StableAcrossGridAndViewModel()
    {
        var rows = CreateRows();
        // 模拟定位链路：VM 设置 SelectedRow = Rows[offset/16]，DataGrid 渲染同一行时再次请求同一索引
        const ulong offset = 0x2A0UL; // 行 42
        var viewModelRow = rows[(int)(offset / 16)];
        Assert.Same(viewModelRow, rows[(int)(offset / 16)]);
        Assert.Equal(offset, viewModelRow.Offset);
    }

    [Fact]
    public void ByteIndexer_ReturnsFormattedHex()
    {
        var data = new byte[32];
        data[10] = 0x13; // 模拟 19 的十六进制字节
        var doc = new HexDocument(new MemoryByteSource(data));
        var rows = new VirtualHexRows(doc);
        Assert.Equal("13", rows[0][10]); // 行 0 第 10 列 = 偏移 0x0A
    }
}
