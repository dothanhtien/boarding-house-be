using Microsoft.AspNetCore.HttpOverrides;

namespace BoardingHouse.Api.Extensions;

public static class ReverseProxyServiceCollectionExtensions
{
    public const string KnownNetworksSection = "ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// Configures UseForwardedHeaders to take the client IP and scheme from X-Forwarded-For / X-Forwarded-Proto,
    /// trusting them only from loopback and the proxy networks listed in ForwardedHeaders:KnownNetworks (CIDR). With
    /// none listed only loopback is trusted, the ASP.NET Core default, so a direct connection (local dev, tests)
    /// behaves as before.
    /// </summary>
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
