using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class ByteRangeTests
{
    [Fact]
    public void EndExclusive_IsOffsetPlusLength()
    {
        var range = new ByteRange(0x1000UL, 0x100UL);
        Assert.Equal(0x1100UL, range.EndExclusive);
    }

    [Fact]
    public void EndExclusive_ThrowsOnOverflow()
    {
        var range = new ByteRange(0xFFFFFFFFFFFFFFF0UL, 0x100UL);
        Assert.Throws<OverflowException>(() => range.EndExclusive);
    }

    [Theory]
    [InlineData(0x1000UL, true)]
    [InlineData(0x10FFUL, true)]
    [InlineData(0x1100UL, false)]
    [InlineData(0x0FFFUL, false)]
    public void Contains_IsHalfOpen(ulong position, bool expected)
    {
        var range = new ByteRange(0x1000UL, 0x100UL);
        Assert.Equal(expected, range.Contains(position));
    }

    [Fact]
    public void Overlaps_AdjacentRangesDoNotOverlap()
    {
        var a = new ByteRange(0x1000UL, 0x100UL);
        var b = new ByteRange(0x1100UL, 0x100UL);
        Assert.False(a.Overlaps(b));
        Assert.False(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_IntersectingRangesDoOverlap()
    {
        var a = new ByteRange(0x1000UL, 0x100UL);
        var b = new ByteRange(0x1080UL, 0x100UL);
        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Overlaps_HighAddressDoesNotOverflow()
    {
        var a = new ByteRange(0xFFFFFFFFFFFFF000UL, 0x1000UL);
        var b = new ByteRange(0xFFFFFFFFFFFFFFFFUL, 1UL);
        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Contains_HighAddressDoesNotOverflow()
    {
        var range = new ByteRange(0xFFFFFFFFFFFFF000UL, 0x1000UL);
        Assert.True(range.Contains(0xFFFFFFFFFFFFFFFFUL));
        Assert.False(range.Contains(0xFFFFFFFFFFFFEFFFUL));
    }

    [Fact]
    public void Overlaps_TouchingAtMaxIsAdjacentNotOverlapping()
    {
        var a = new ByteRange(0xFFFFFFFFFFFFF000UL, 0x1000UL);
        var b = new ByteRange(0x0000000000000000UL, 1UL); // 完全在左侧
        Assert.False(a.Overlaps(b));
    }
}
