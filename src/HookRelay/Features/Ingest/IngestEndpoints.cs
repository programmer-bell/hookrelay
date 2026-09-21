using System.Text;
using HookRelay.Data;
using HookRelay.Domain;
using HookRelay.Services;

namespace HookRelay.Features.Ingest;

public static class IngestEndpoints
{
    private const int MaxBodyBytes = 256 * 1024;

    private static readonly string[] IngestMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/h/{slug}", IngestMethods, HandleIngestAsync)
            .RequireRateLimiting("ingest");
    }

    private static async Task<IResult> HandleIngestAsync(
        string slug,
        HttpRequest request,
        EndpointRepository endpoints,
        CaptureRepository captures,
        EventBus eventBus,
        CancellationToken ct)
    {
        var endpoint = await endpoints.GetBySlugAsync(slug, ct);
        if (endpoint is null)
        {
            return Results.NotFound();
        }

        if (request.ContentLength is > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyWithLimitAsync(request.Body, MaxBodyBytes, ct);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var idempotencyKey = request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length > 0)
        {
            var existing = await captures.FindByIdempotencyKeyAsync(endpoint.Id, idempotencyKey, ct);
            if (existing is not null)
            {
                return Results.Json(new { id = existing.Id }, statusCode: StatusCodes.Status200OK);
            }
        }

        var receivedAt = TimeProvider.System.GetUtcNow().UtcDateTime;
        var captured = new CapturedRequest(
            Guid.NewGuid(),
            endpoint.Id,
            request.Method,
            HeaderRedaction.ToJson(request.Headers),
            body,
            request.QueryString.HasValue ? request.QueryString.Value : null,
            idempotencyKey.Length > 0 ? idempotencyKey : null,
            receivedAt);

        await captures.CaptureAsync(captured, ct);
        eventBus.Publish(new RequestCapturedEvent(
            captured.Id,
            endpoint.Id,
            endpoint.Slug,
            request.Method,
            receivedAt));

        return Results.Json(new { id = captured.Id }, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<string?> ReadBodyWithLimitAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (total <= maxBytes)
        {
            var remaining = maxBytes + 1 - total;
            var read = await body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), ct);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            buffer.Write(chunk, 0, read);
            total += read;
        }

        return null;
    }
}
