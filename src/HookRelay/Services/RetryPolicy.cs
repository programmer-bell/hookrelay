namespace HookRelay.Services;

public sealed class RetryPolicy
{
    private readonly int _initialDelaySeconds;
    private readonly int _maxDelaySeconds;

    public RetryPolicy(int initialDelaySeconds = 5, int maxDelaySeconds = 3600)
    {
        _initialDelaySeconds = initialDelaySeconds;
        _maxDelaySeconds = maxDelaySeconds;
    }

    public TimeSpan NextDelay(int completedAttempt, Random random)
    {
        var baseSeconds = Math.Min(_initialDelaySeconds * Math.Pow(2, completedAttempt), _maxDelaySeconds);
        var jitter = 1 + random.NextDouble() * 0.2;
        return TimeSpan.FromSeconds(baseSeconds * jitter);
    }

    public static bool IsExhausted(int completedAttempt, int maxAttempts) =>
        completedAttempt >= maxAttempts;
}
