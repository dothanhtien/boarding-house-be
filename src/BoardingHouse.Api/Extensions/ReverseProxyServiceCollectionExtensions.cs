using Microsoft.AspNetCore.HttpOverrides;

namespace BoardingHouse.Api.Extensions;

public static class ReverseProxyServiceCollectionExtensions
{
    public const string KnownNetworksSection = "ForwardedHeaders:KnownNetworks";

    // Forwarded headers are trusted only from loopback and the listed proxy networks (CIDR): a client cannot spoof its
    // IP by sending X-Forwarded-For itself
    public static IServiceCollection AddReverseProxySupport(
        this IServiceCollection services, IConfiguration configuration)
    {
        var knownNetworks = configuration.GetSection(KnownNetworksSection).Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var network in knownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
