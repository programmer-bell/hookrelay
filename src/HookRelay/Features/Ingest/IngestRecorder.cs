using HookRelay.Data;
using HookRelay.Domain;
using HookRelay.Services;

namespace HookRelay.Features.Ingest;

public enum IngestOutcome
{
    EndpointNotFound,
    Existing,
    Captured,
}

public sealed record IngestResult(IngestOutcome Outcome, Guid? RequestId, Guid EndpointId, string? Slug);

public sealed class IngestRecorder
{
    private readonly IEndpointRepository _endpoints;
    private readonly ICaptureRepository _captures;
    private readonly EventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public IngestRecorder(
        IEndpointRepository endpoints,
        ICaptureRepository captures,
        EventBus eventBus,
        TimeProvider timeProvider)
    {
        _endpoints = endpoints;
        _captures = captures;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<IngestResult> RecordAsync(
        string slug,
        string method,
        string headersJson,
        string body,
        string? query,
        string idempotencyKeyRaw,
        CancellationToken ct = default)
    {
        var endpoint = await _endpoints.GetBySlugAsync(slug, ct);
        if (endpoint is null)
        {
            return new IngestResult(IngestOutcome.EndpointNotFound, null, Guid.Empty, null);
        }

        var idempotencyKey = idempotencyKeyRaw.Trim();
        if (idempotencyKey.Length > 0)
        {
            var existing = await _captures.FindByIdempotencyKeyAsync(endpoint.Id, idempotencyKey, ct);
            if (existing is not null)
            {
                return new IngestResult(IngestOutcome.Existing, existing.Id, endpoint.Id, endpoint.Slug);
            }
        }

        var receivedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var captured = new CapturedRequest(
            Guid.NewGuid(),
            endpoint.Id,
            method,
            headersJson,
            body,
            query,
            idempotencyKey.Length > 0 ? idempotencyKey : null,
            receivedAt);

        await _captures.CaptureAsync(captured, ct);
        _eventBus.Publish(new RequestCapturedEvent(
            captured.Id,
            endpoint.Id,
            endpoint.Slug,
            method,
            captured.Headers,
            captured.Body,
            captured.Query,
            receivedAt));

        return new IngestResult(IngestOutcome.Captured, captured.Id, endpoint.Id, endpoint.Slug);
    }
}
