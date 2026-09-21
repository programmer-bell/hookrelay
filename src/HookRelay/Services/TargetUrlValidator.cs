using System.Net;
using System.Net.Sockets;

namespace HookRelay.Services;

public sealed record UrlValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class TargetUrlValidator
{
    public UrlValidationResult Validate(string? targetUrl)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            errors.Add("Target URL is required.");
            return new UrlValidationResult(false, errors);
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            errors.Add("Target URL must be an absolute URL such as https://example.com/hooks.");
            return new UrlValidationResult(false, errors);
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            errors.Add("Target URL must use http or https.");
        }

        var host = uri.Host.Trim('[', ']');
        if (string.IsNullOrEmpty(host))
        {
            errors.Add("Target URL host is invalid.");
        }

        if (errors.Count == 0 && IPAddress.TryParse(host, out var literal) && IsBlockedAddress(literal))
        {
            errors.Add("Target URL points to a private or loopback address and is not allowed.");
        }

        return new UrlValidationResult(errors.Count == 0, errors);
    }

    public async Task<UrlValidationResult> ValidateAsync(string? targetUrl, CancellationToken ct = default)
    {
        var result = Validate(targetUrl);
        if (!result.IsValid)
        {
            return result;
        }

        var host = new Uri(targetUrl!).Host.Trim('[', ']');

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Any(IsBlockedAddress))
            {
                return new UrlValidationResult(false, ["Target URL resolves to a private or loopback address and is not allowed."]);
            }
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return new UrlValidationResult(false, ["Target URL host could not be resolved."]);
        }

        return result;
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 127) return true;
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }

        return true;
    }
}
