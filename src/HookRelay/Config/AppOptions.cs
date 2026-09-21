namespace HookRelay.Config;

public sealed class AppOptions
{
    public const string SectionName = "Delivery";

    public int MaxAttempts { get; init; } = 6;

    public int TimeoutSeconds { get; init; } = 10;

    public int PollIntervalSeconds { get; init; } = 2;

    public int InitialDelaySeconds { get; init; } = 5;

    public int MaxDelaySeconds { get; init; } = 3600;
}
