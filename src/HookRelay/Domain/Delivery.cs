namespace HookRelay.Domain;

public sealed record Delivery(
    Guid Id,
    Guid RequestId,
    string Status,
    int AttemptCount,
    DateTime NextAttemptAt,
    DateTime UpdatedAt);
