namespace HookRelay.Services;

public sealed record RequestCapturedEvent(
    Guid CapturedRequestId,
    Guid EndpointId,
    string Slug,
    string Method,
    string Headers,
    string Body,
    string? Query,
    DateTime ReceivedAt);
