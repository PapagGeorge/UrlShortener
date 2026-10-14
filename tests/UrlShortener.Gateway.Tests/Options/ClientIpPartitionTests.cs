using System.Net;
using UrlShortener.Gateway.RateLimiting;

namespace UrlShortener.Gateway.Tests.Options;

public sealed class ClientIpPartitionTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("10.0.0.1", "10.0.0.1")]
    public void IPv4_IsPartitionedPerAddress(string address, string expected)
    {
        ClientIpPartition.KeyFor(IPAddress.Parse(address)).ShouldBe(expected);
    }

    [Fact]
    public void IPv6_AddressesInTheSame64Prefix_ShareOnePartition()
    {
        // A single customer owns a whole /64, so rotating the low 64 bits must not buy extra requests.
        var a = ClientIpPartition.KeyFor(IPAddress.Parse("2001:db8:abcd:12:1111:2222:3333:4444"));
        var b = ClientIpPartition.KeyFor(IPAddress.Parse("2001:db8:abcd:12:ffff:eeee:dddd:cccc"));

        a.ShouldBe(b);
        a.ShouldBe("2001:db8:abcd:12::/64");
    }

    [Fact]
    public void IPv6_DifferentPrefixes_AreDifferentPartitions()
    {
        ClientIpPartition.KeyFor(IPAddress.Parse("2001:db8:abcd:12::1"))
            .ShouldNotBe(ClientIpPartition.KeyFor(IPAddress.Parse("2001:db8:abcd:13::1")));
    }

    [Fact]
    public void IPv4MappedIPv6_IsTreatedAsIPv4()
    {
        ClientIpPartition.KeyFor(IPAddress.Parse("::ffff:203.0.113.7")).ShouldBe("203.0.113.7");
    }

    [Fact]
    public void UnknownAddress_SharesOnePartition_RatherThanBypassingTheLimit()
    {
        ClientIpPartition.KeyFor(null).ShouldBe("unknown");
    }
}
