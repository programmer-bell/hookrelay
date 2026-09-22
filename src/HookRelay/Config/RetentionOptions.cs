namespace HookRelay.Config;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public int RetentionDays { get; init; } = 7;

    public int CheckIntervalHours { get; init; } = 1;
}
