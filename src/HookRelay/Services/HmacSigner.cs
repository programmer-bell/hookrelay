using System.Security.Cryptography;
using System.Text;

namespace HookRelay.Services;

public static class HmacSigner
{
    public static string Sign(string secret, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
