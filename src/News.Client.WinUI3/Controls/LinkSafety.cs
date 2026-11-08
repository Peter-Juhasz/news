using System.Net;
using System.Net.Sockets;

namespace News.Client.WinUI3.Controls;

/// <summary>
/// Decides which addresses from untrusted content may be followed: https only, written in plain
/// ASCII, and pointing somewhere other than this machine.
/// </summary>
/// <remarks>
/// Non-ASCII is refused outright rather than judged: a letter that only looks like another is how
/// an address passes for one it is not, and the reader cannot be expected to tell them apart.
/// Loopback is refused because a link from a feed has no business reaching servers that only
/// listen to this machine.
/// </remarks>
public static class LinkSafety
{
    /// <summary>Parses <paramref name="url"/> into an address that is safe to follow, or null.</summary>
    public static Uri? TryCreate(string? url) =>
        url is { Length: > 0 } &&
        IsAscii(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        IsSafe(uri)
            ? uri
            : null;

    /// <summary>Whether <paramref name="uri"/> is safe to follow.</summary>
    public static bool IsSafe(Uri uri) =>
        uri is { IsAbsoluteUri: true } &&
        uri.Scheme == Uri.UriSchemeHttps &&
        IsAscii(uri.OriginalString) &&
        !IsLoopback(uri);

    /// <summary>Whether <paramref name="value"/> is printable ASCII throughout.</summary>
    public static bool IsAscii(string value) => !value.AsSpan().ContainsAnyExceptInRange(' ', '~');

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        var host = uri.IdnHost.TrimEnd('.');

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        // the unspecified address reaches this machine too on most systems
        return IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.AddressFamily is AddressFamily.InterNetwork && address.GetAddressBytes()[0] is 0 or 127;
    }
}
