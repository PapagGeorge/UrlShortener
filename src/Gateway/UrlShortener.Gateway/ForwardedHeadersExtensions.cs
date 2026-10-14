using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using UrlShortener.Gateway.Options;

namespace UrlShortener.Gateway;

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Trusts <c>X-Forwarded-For</c> only from the configured proxies. ASP.NET Core trusts loopback by default; that is cleared here, so with
    /// nothing configured every forwarded header is ignored and the socket address is the client (the rate limit cannot be spoofed).
    /// Note that an <em>empty</em> trusted list does not mean "trust nobody" to ASP.NET Core: see the comment below.
    /// </summary>
    public static IServiceCollection AddGatewayForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();
        services.AddOptions<TrustedProxyOptions>().Bind(configuration.GetSection(TrustedProxyOptions.SectionName)).ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxyOptions>>((forwarded, trusted) =>
        {
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();

            foreach (var proxy in trusted.Value.ParseProxies())
            {
                forwarded.KnownProxies.Add(proxy);
            }

            foreach (var network in trusted.Value.ParseNetworks())
            {
                forwarded.KnownIPNetworks.Add(network);
            }

            // Pitfall: when BOTH lists are empty the middleware trusts every sender (the opposite of "trust nobody").
            // So with nothing configured it is switched off entirely, and the socket address stays the client address.
            forwarded.ForwardedHeaders = forwarded.KnownProxies.Count + forwarded.KnownIPNetworks.Count == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        return services;
    }
}
