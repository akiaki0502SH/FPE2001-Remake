using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class SearchEngineTests
{
    private static HexDocument Doc(params byte[] data) => new(new MemoryByteSource(data));

    [Fact]
    public void ParseHexPattern_WithWildcardsAndSpaces()
    {
        var pattern = SearchEngine.ParseHexPattern("DE AD ?? EF");
        Assert.Equal(4, pattern.Length);
        Assert.Equal((byte)0xDE, pattern[0]);
        Assert.Null(pattern[2]);
        Assert.Equal((byte)0xEF, pattern[3]);

        var compact = SearchEngine.ParseHexPattern("DEAD??EF");
        Assert.Equal(4, compact.Length);
        Assert.Null(compact[2]);
    }

    [Fact]
    public void ParseHexPattern_RejectsOddLength()
    {
        Assert.Throws<ArgumentException>(() => SearchEngine.ParseHexPattern("DEA"));
    }

    [Fact]
    public void FindHex_ExactHit()
    {
        using var doc = Doc(0x00, 0x11, 0xDE, 0xAD, 0xBE, 0xEF, 0x22);
        var engine = new SearchEngine();
        var hit = engine.FindHex(doc, SearchEngine.ParseHexPattern("DE AD BE EF"), 0);
        Assert.NotNull(hit);
        Assert.Equal(2UL, hit!.Offset);
    }

    [Fact]
    public void FindHex_WildcardMask()
    {
        using var doc = Doc(0x00, 0x11, 0xDE, 0x00, 0xBE, 0xEF, 0x22);
        var engine = new SearchEngine();
        var hit = engine.FindHex(doc, SearchEngine.ParseHexPattern("DE ?? BE EF"), 0);
        Assert.NotNull(hit);
        Assert.Equal(2UL, hit!.Offset);
    }

    [Fact]
    public void FindHex_CrossBlockBoundary()
    {
        // 模式跨 256KiB 块边界（重叠保留）
        var data = new byte[SearchBlockSize + 16];
        data[SearchBlockSize - 2] = 0xAA;
        data[SearchBlockSize - 1] = 0xBB;
        data[SearchBlockSize] = 0xCC;
        data[SearchBlockSize + 1] = 0xDD;
        using var doc = Doc(data);
        var engine = new SearchEngine();
        var hit = engine.FindHex(doc, SearchEngine.ParseHexPattern("AA BB CC DD"), 0);
        Assert.NotNull(hit);
        Assert.Equal((ulong)SearchBlockSize - 2, hit!.Offset);
    }

    private const int SearchBlockSize = 256 * 1024;

    [Fact]
    public void FindText_AsciiAndCase()
    {
        using var doc = Doc("Hello FPE2001 World"u8.ToArray());
        var engine = new SearchEngine();
        var hit = engine.FindText(doc, "fpe2001", "ASCII", matchCase: false, 0);
        Assert.NotNull(hit);
        Assert.Equal(6UL, hit!.Offset);
    }

    [Fact]
    public void FindText_GbkEncoding()
    {
        // "游戏" GBK: D3 CE CF B7
        var data = new byte[] { 0x41, 0x42, 0xD3, 0xCE, 0xCF, 0xB7, 0x43 };
        using var doc = Doc(data);
        var engine = new SearchEngine();
        var hit = engine.FindText(doc, "游戏", "GBK", matchCase: true, 0);
        Assert.NotNull(hit);
        Assert.Equal(2UL, hit!.Offset);
    }

    [Fact]
    public void FindInteger_BigEndian()
    {
        using var doc = Doc(0x00, 0x00, 0x12, 0x34, 0x56, 0x78, 0x00);
        var engine = new SearchEngine();
        var hit = engine.FindInteger(doc, 0x12345678UL, 4, Endianness.BigEndian, 0);
        Assert.NotNull(hit);
        Assert.Equal(2UL, hit!.Offset);
    }

    [Fact]
    public void FindFloat_Float32()
    {
        var bytes = BitConverter.GetBytes(3.5f); // little-endian 00 00 60 40
        var data = new byte[] { 0xFF, 0xFF, bytes[0], bytes[1], bytes[2], bytes[3], 0x00 };
        using var doc = Doc(data);
        var engine = new SearchEngine();
        var hit = engine.FindFloat(doc, 3.5, 4, Endianness.LittleEndian, 0);
        Assert.NotNull(hit);
        Assert.Equal(2UL, hit!.Offset);
    }
}
