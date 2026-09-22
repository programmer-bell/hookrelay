using HookRelay.Domain;
using HookRelay.Features.Inspector;
using Xunit;

namespace HookRelay.Tests;

public sealed class StatusFilterTests
{
    [Theory]
    [InlineData(null, "all")]
    [InlineData("", "all")]
    [InlineData("all", "all")]
    [InlineData("pending", "pending")]
    [InlineData("succeeded", "succeeded")]
    [InlineData("dead", "dead")]
    [InlineData("bogus", "all")]
    [InlineData("ALL", "all")]
    public void Normalize_ReturnsKnownFilterOrDefault(string? input, string expected)
    {
        Assert.Equal(expected, StatusFilter.Normalize(input));
    }

    [Theory]
    [InlineData("all", false)]
    [InlineData("pending", true)]
    [InlineData("dead", true)]
    public void Normalize_AllowsOnlyTerminalInspectionStates(string input, bool isExpectedStatus)
    {
        var normalized = StatusFilter.Normalize(input);
        var isInspectable = normalized is DeliveryStatus.Pending ||
                            normalized is DeliveryStatus.Succeeded ||
                            normalized is DeliveryStatus.Dead;
        Assert.Equal(isExpectedStatus, isInspectable);
    }
}
