using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class AddressFormattingTests
{
    [Theory]
    [InlineData(0UL, "0x0000000000000000")]
    [InlineData(0x1000UL, "0x0000000000001000")]
    [InlineData(0xFFFFFFFFFFFFFFFFUL, "0xFFFFFFFFFFFFFFFF")]
    [InlineData(0x0000000112345678UL, "0x0000000112345678")]
    public void ToHex16_AlwaysProducesZeroPadded16Digits(ulong value, string expected)
    {
        Assert.Equal(expected, AddressFormatting.ToHex16(value));
    }

    [Theory]
    [InlineData("0x0000000000001000", 0x1000UL)]
    [InlineData("0000000000001000", 0x1000UL)]
    [InlineData("0X1000", 0x1000UL)]
    [InlineData("&H1000", 0x1000UL)]
    [InlineData("FFFFFFFFFFFFFFFF", 0xFFFFFFFFFFFFFFFFUL)]
    public void TryParseHex_AcceptsPrefixAndBareForms(string text, ulong expected)
    {
        Assert.True(AddressFormatting.TryParseHex(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("0xZZZZ")]
    [InlineData("12345678901234567890")]
    public void TryParseHex_RejectsInvalid(string text)
    {
        Assert.False(AddressFormatting.TryParseHex(text, out _));
    }

    [Fact]
    public void HexJson_RoundTripsMaxUInt64()
    {
        const ulong value = 0xFFFFFFFFFFFFFFFFUL;
        var json = AddressFormatting.ToHexJson(value);
        Assert.Equal("FFFFFFFFFFFFFFFF", json);
        Assert.Equal(value, AddressFormatting.ParseHexJson(json));
    }

    [Fact]
    public void OffsetDisplay_ShowsHexAndDecimal()
    {
        var display = AddressFormatting.ToOffsetDisplay(0x1000UL);
        Assert.Contains("0x0000000000001000", display);
        Assert.Contains("4096", display);
    }
}
