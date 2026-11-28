using System.Net;
using BoardingHouse.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoardingHouse.UnitTests.Extensions;

public class ReverseProxyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddReverseProxySupport_WithKnownNetworks_TrustsThem()
    {
        var options = BuildOptions(new() { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8" });

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Contains(System.Net.IPNetwork.Parse("10.0.0.0/8"), options.KnownIPNetworks);
    }

    [Fact]
    public void AddReverseProxySupport_WithoutKnownNetworks_KeepsLoopbackDefaults()
    {
        var options = BuildOptions([]);

        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.Contains(System.Net.IPNetwork.Parse("127.0.0.0/8"), options.KnownIPNetworks);
        Assert.DoesNotContain(System.Net.IPNetwork.Parse("10.0.0.0/8"), options.KnownIPNetworks);
    }

    [Fact]
    public void AddReverseProxySupport_WithMalformedNetwork_Throws()
    {
        Assert.ThrowsAny<FormatException>(() =>
            BuildOptions(new() { ["ForwardedHeaders:KnownNetworks:0"] = "not-a-cidr" }));
    }

    private static ForwardedHeadersOptions BuildOptions(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddReverseProxySupport(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }
}
