using HookRelay.Services;
using Xunit;

namespace HookRelay.Tests;

public sealed class HmacSignerTests
{
    [Fact]
    public void Sign_MatchesKnownRfc4231SelfTestVector()
    {
        var signature = HmacSigner.Sign("key", "The quick brown fox jumps over the lazy dog");

        Assert.Equal(
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            signature);
    }

    [Fact]
    public void Sign_IsDeterministic()
    {
        var first = HmacSigner.Sign("secret", "timestamp.body");
        var second = HmacSigner.Sign("secret", "timestamp.body");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Sign_ReturnsLowercaseHex()
    {
        var signature = HmacSigner.Sign("secret", "message");

        Assert.Equal(64, signature.Length);
        Assert.All(signature, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    }
}
