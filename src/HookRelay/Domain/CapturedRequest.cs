namespace HookRelay.Domain;

public sealed record CapturedRequest(
    Guid Id,
    Guid EndpointId,
    string Method,
    string Headers,
    string Body,
    string? Query,
    string? IdempotencyKey,
    DateTime ReceivedAt);
