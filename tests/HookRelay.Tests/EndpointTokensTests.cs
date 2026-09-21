using HookRelay.Features.Endpoints;
using Xunit;

namespace HookRelay.Tests;

public sealed class EndpointTokensTests
{
    [Fact]
    public void NewSlug_ReturnsTenCharacters()
    {
        Assert.Equal(10, EndpointTokens.NewSlug().Length);
    }

    [Fact]
    public void NewSlug_UsesUrlSafeLowercaseAlphabet()
    {
        var slug = EndpointTokens.NewSlug();

        Assert.All(slug, c => Assert.True(c is (>= 'a' and <= 'z') or (>= '0' and <= '9')));
    }

    [Fact]
    public void NewSlug_GeneratesDifferentValues()
    {
        Assert.NotEqual(EndpointTokens.NewSlug(), EndpointTokens.NewSlug());
    }

    [Fact]
    public void NewSigningSecret_IsSixtyFourHexCharacters()
    {
        var secret = EndpointTokens.NewSigningSecret();

        Assert.Equal(64, secret.Length);
        Assert.Equal(32, Convert.FromHexString(secret).Length);
    }

    [Fact]
    public void NewSigningSecret_GeneratesDifferentValues()
    {
        Assert.NotEqual(EndpointTokens.NewSigningSecret(), EndpointTokens.NewSigningSecret());
    }
}
