using HookRelay.Domain;

namespace HookRelay.Data;

public sealed record RequestAttempt(Guid RequestId, DeliveryAttempt Attempt);
