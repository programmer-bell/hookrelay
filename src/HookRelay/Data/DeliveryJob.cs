namespace HookRelay.Data;

public sealed record DeliveryJob(
    Guid DeliveryId,
    Guid RequestId,
    string Slug,
    string SigningSecret,
    string TargetUrl,
    string Headers,
    string Body,
    int AttemptCount);
