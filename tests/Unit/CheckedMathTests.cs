using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class CheckedMathTests
{
    [Fact]
    public void Add_NormalValues()
    {
        Assert.Equal(0x2000UL, CheckedMath.Add(0x1000UL, 0x1000UL));
    }

    [Fact]
    public void Add_ThrowsOnOverflow()
    {
        Assert.Throws<OverflowException>(() => CheckedMath.Add(ulong.MaxValue, 1UL));
    }

    [Fact]
    public void AddLength_ThrowsWhenOffsetPlusLengthOverflows()
    {
        Assert.Throws<OverflowException>(() => CheckedMath.AddLength(0xFFFFFFFFFFFFFFF0UL, 0x100UL));
    }

    [Fact]
    public void AddLength_SupportsBeyond4GiB()
    {
        // 规格 §12：文件偏移超过 4GB 必须支持
        var high = 0x0000000200000000UL; // 8 GiB
        Assert.Equal(0x0000000200001000UL, CheckedMath.AddLength(high, 0x1000UL));
    }
}
