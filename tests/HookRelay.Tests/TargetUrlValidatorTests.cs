using HookRelay.Services;
using Xunit;

namespace HookRelay.Tests;

public sealed class TargetUrlValidatorTests
{
    private static UrlValidationResult Validate(string? targetUrl) =>
        new TargetUrlValidator().Validate(targetUrl);

    [Fact]
    public void PublicHttpAndHttpsUrls_AreAccepted()
    {
        Assert.True(Validate("https://example.com/hooks").IsValid);
        Assert.True(Validate("http://example.com/hooks").IsValid);
    }

    [Fact]
    public void PublicUrlWithPortAndPath_IsAccepted()
    {
        Assert.True(Validate("http://api.example.org:8080/api/v1/hook").IsValid);
    }

    [Fact]
    public void PublicIpNotInBlockedRanges_IsAccepted()
    {
        Assert.True(Validate("http://192.0.2.1/hook").IsValid);
        Assert.True(Validate("http://172.32.0.1/hook").IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingUrl_IsRejected(string? targetUrl)
    {
        Assert.False(Validate(targetUrl).IsValid);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("gopher://example.com/_x")]
    public void NonHttpSchemes_AreRejected(string targetUrl)
    {
        Assert.False(Validate(targetUrl).IsValid);
    }

    [Theory]
    [InlineData("example.com/hooks")]
    [InlineData("not a url")]
    public void MissingScheme_IsRejected(string targetUrl)
    {
        Assert.False(Validate(targetUrl).IsValid);
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://127.0.0.2/hook")]
    [InlineData("http://10.0.0.1/hook")]
    [InlineData("http://172.16.0.1/hook")]
    [InlineData("http://172.31.255.255/hook")]
    [InlineData("http://192.168.0.1/hook")]
    [InlineData("http://169.254.0.1/hook")]
    public void PrivateAndLoopbackIpLiterals_AreRejected(string targetUrl)
    {
        Assert.False(Validate(targetUrl).IsValid);
    }

    [Theory]
    [InlineData("http://[::1]/hook")]
    [InlineData("http://[fc00::1]/hook")]
    [InlineData("http://[fd12:3456:789a::1]/hook")]
    [InlineData("http://[fe80::1]/hook")]
    public void Ipv6LoopbackAndPrivateAddresses_AreRejected(string targetUrl)
    {
        Assert.False(Validate(targetUrl).IsValid);
    }
}
