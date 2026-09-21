using HookRelay.Features.Inspector;
using Xunit;

namespace HookRelay.Tests;

public sealed class RequestFormatterTests
{
    [Fact]
    public void PrettyPrintBody_IndentsJson()
    {
        var result = RequestFormatter.PrettyPrintBody("{\"a\":1,\"b\":[true]}");

        Assert.Contains("\n", result);
        Assert.Contains("  \"a\": 1", result);
        Assert.Contains("  \"b\": [", result);
    }

    [Fact]
    public void PrettyPrintBody_ReturnsRawTextUnchanged()
    {
        const string body = "plain text, not json";

        Assert.Equal(body, RequestFormatter.PrettyPrintBody(body));
    }

    [Fact]
    public void PrettyPrintBody_ReturnsEmptyUnchanged()
    {
        Assert.Equal(string.Empty, RequestFormatter.PrettyPrintBody(string.Empty));
    }

    [Fact]
    public void PrettyPrintBody_ReturnsRawForInvalidJson()
    {
        const string body = "{\"broken\"";

        Assert.Equal(body, RequestFormatter.PrettyPrintBody(body));
    }

    [Fact]
    public void ParseHeaders_ReadsObjectProperties()
    {
        var headers = RequestFormatter.ParseHeaders("""{"Host": "localhost", "Authorization": "[redacted]"}""");

        Assert.Equal(2, headers.Count);
        Assert.Contains(headers, h => h.Name == "Host" && h.Value == "localhost");
        Assert.Contains(headers, h => h.Name == "Authorization" && h.Value == "[redacted]");
    }

    [Fact]
    public void ParseHeaders_ReturnsEmptyForInvalidJson()
    {
        Assert.Empty(RequestFormatter.ParseHeaders("not json"));
        Assert.Empty(RequestFormatter.ParseHeaders(""));
    }
}
