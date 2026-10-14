using System.Net;

namespace UrlShortener.Gateway.RateLimiting;

/// <summary>Maps a client address to its rate-limit bucket.</summary>
public static class ClientIpPartition
{
    public const string Unknown = "unknown";

    public static string KeyFor(IPAddress? address)
    {
        if (address is null)
        {
            // No address (should not happen behind Kestrel): share one bucket rather than letting such requests through unlimited.
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        // One customer typically owns a whole /64, so rotating the low 64 bits must not buy extra requests.
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
