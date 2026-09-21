using HookRelay.Domain;

namespace HookRelay.Features.Inspector;

public sealed record RequestRow(
    Guid Id,
    string Method,
    string Headers,
    string Body,
    string? Query,
    DateTime ReceivedAt,
    string DeliveryStatus)
{
    public IReadOnlyList<DeliveryAttempt> Attempts { get; init; } = [];
}
