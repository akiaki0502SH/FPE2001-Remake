using System.Text.Json;
using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class LogicalAddressTests
{
    [Fact]
    public void Json_RoundTripsAllFields()
    {
        var address = new LogicalAddress(
            AddressSpace.GuestVirtual,
            "domain:test",
            0x0000000112345678UL,
            Endianness.BigEndian,
            32);

        var json = JsonSerializer.Serialize(address);
        var back = JsonSerializer.Deserialize<LogicalAddress>(json);

        Assert.Equal(address, back);
    }

    [Fact]
    public void Json_ValueIsHexStringNotNumber()
    {
        var address = new LogicalAddress(AddressSpace.HostVirtual, "", 0x1000UL);
        var json = JsonSerializer.Serialize(address);

        // Value 必须为十六进制字符串（规格 §12：JSON 中的 ulong 使用十六进制字符串）
        Assert.Contains("\"Value\":\"0000000000001000\"", json);
        Assert.DoesNotContain("\"Value\":4096", json);
    }

    [Fact]
    public void Json_HandlesMaxUInt64()
    {
        var address = new LogicalAddress(AddressSpace.HostVirtual, "", 0xFFFFFFFFFFFFFFFFUL);
        var back = JsonSerializer.Deserialize<LogicalAddress>(JsonSerializer.Serialize(address));
        Assert.Equal(0xFFFFFFFFFFFFFFFFUL, back.Value);
    }

    [Fact]
    public void ToString_ShowsSpaceDomainAndHexValue()
    {
        var address = new LogicalAddress(AddressSpace.HostVirtual, "proc", 0x1000UL);
        Assert.Equal("HostVirtual:proc:0x0000000000001000", address.ToString());
    }
}
