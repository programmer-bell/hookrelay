namespace HookRelay.Services;

public sealed record DeliveryStatusChangedEvent(
    Guid DeliveryId,
    Guid RequestId,
    string Slug,
    string Status);
