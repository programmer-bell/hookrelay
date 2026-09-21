using System.Text.Json;
using HookRelay.Features.Ingest;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HookRelay.Tests;

public sealed class HeaderRedactionTests
{
    private static readonly string[] TwoValues = ["a", "b"];

    private static string? GetValue(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    [Fact]
    public void AuthorizationHeader_IsRedacted()
    {
        var headers = new HeaderDictionary { { "Authorization", "Bearer super-secret-token" } };

        var json = HeaderRedaction.ToJson(headers);

        Assert.Equal("[redacted]", GetValue(json, "Authorization"));
    }

    [Fact]
    public void CookieHeader_IsRedacted()
    {
        var headers = new HeaderDictionary { { "Cookie", "session=abc123" } };

        var json = HeaderRedaction.ToJson(headers);

        Assert.Equal("[redacted]", GetValue(json, "Cookie"));
    }

    [Fact]
    public void OrdinaryHeaders_AreStoredUnchanged()
    {
        var headers = new HeaderDictionary { { "Content-Type", "application/json" } };

        var json = HeaderRedaction.ToJson(headers);

        Assert.Equal("application/json", GetValue(json, "Content-Type"));
    }

    [Fact]
    public void MultiValueHeader_IsJoined()
    {
        var headers = new HeaderDictionary { { "X-Multi", new Microsoft.Extensions.Primitives.StringValues(TwoValues) } };

        var json = HeaderRedaction.ToJson(headers);

        Assert.Equal("a, b", GetValue(json, "X-Multi"));
    }
}
