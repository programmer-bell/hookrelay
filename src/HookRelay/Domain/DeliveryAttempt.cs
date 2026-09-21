namespace HookRelay.Domain;

public sealed record DeliveryAttempt(
    int? StatusCode,
    int? DurationMs,
    string? Error,
    DateTime AttemptedAt);
