namespace HookRelay.Features.Endpoints;

public sealed record EndpointFormModel(
    string Name,
    string TargetUrl,
    IReadOnlyList<string> Errors);
