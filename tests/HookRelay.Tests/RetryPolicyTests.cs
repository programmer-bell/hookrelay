using HookRelay.Services;
using Xunit;

namespace HookRelay.Tests;

public sealed class RetryPolicyTests
{
    private const int InitialDelaySeconds = 5;
    private const int MaxDelaySeconds = 3600;

    private static readonly RetryPolicy Policy = new(InitialDelaySeconds, MaxDelaySeconds);

    [Fact]
    public void NextDelay_StaysWithinCap()
    {
        var random = new Random(42);

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var delay = Policy.NextDelay(attempt, random);

            Assert.InRange(delay.TotalSeconds, 0, MaxDelaySeconds * 1.2);
        }
    }

    [Fact]
    public void NextDelay_IsWithinJitterBounds()
    {
        var random = new Random(7);
        var baseSeconds = InitialDelaySeconds * Math.Pow(2, 3);

        for (var i = 0; i < 1000; i++)
        {
            var delay = Policy.NextDelay(3, random);

            Assert.InRange(delay.TotalSeconds, baseSeconds, baseSeconds * 1.2);
        }
    }

    [Fact]
    public void NextDelay_GrowsUntilCapped()
    {
        var random = new Random(1);
        var previous = TimeSpan.Zero;

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var delay = Policy.NextDelay(attempt, random);

            Assert.True(delay >= previous, $"Attempt {attempt} delay did not grow: {delay} < {previous}");
            previous = delay;
        }
    }

    [Fact]
    public void NextDelay_UsesConfiguredDelays()
    {
        var policy = new RetryPolicy(initialDelaySeconds: 2, maxDelaySeconds: 8);
        var random = new Random(3);

        var delay = policy.NextDelay(1, random);

        Assert.InRange(delay.TotalSeconds, 4, 4 * 1.2);
    }

    [Theory]
    [InlineData(1, 6, false)]
    [InlineData(5, 6, false)]
    [InlineData(6, 6, true)]
    [InlineData(7, 6, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    public void IsExhausted_ReflectsMaxAttempts(int attempt, int max, bool expected)
    {
        Assert.Equal(expected, RetryPolicy.IsExhausted(attempt, max));
    }
}
