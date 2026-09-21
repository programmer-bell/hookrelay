namespace HookRelay.Domain;

public sealed record Endpoint(
    Guid Id,
    string Slug,
    string Name,
    string TargetUrl,
    string SigningSecret,
    DateTime CreatedAt);
