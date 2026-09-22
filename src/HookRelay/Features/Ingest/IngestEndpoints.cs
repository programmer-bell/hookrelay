using System.Text;

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

    private static ILogger GetLogger(ILoggerFactory factory) =>
        factory.CreateLogger(typeof(IngestEndpoints).FullName ?? nameof(IngestEndpoints));

    private static async Task<IResult> HandleIngestAsync(
        string slug,
        HttpRequest request,
        IngestRecorder recorder,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = GetLogger(loggerFactory);

        if (request.ContentLength is > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyWithLimitAsync(request.Body, MaxBodyBytes, ct);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var result = await recorder.RecordAsync(
            slug,
            request.Method,
            HeaderRedaction.ToJson(request.Headers),
            body,
            request.QueryString.HasValue ? request.QueryString.Value : null,
            request.Headers["Idempotency-Key"].ToString().Trim(),
            ct);

        if (result.Outcome == IngestOutcome.EndpointNotFound)
        {
            return Results.NotFound();
        }

        if (result.Outcome == IngestOutcome.Existing)
        {
            return Results.Json(new { id = result.RequestId }, statusCode: StatusCodes.Status200OK);
        }

        LogCaptured(logger, result.RequestId!.Value, result.EndpointId, result.Slug!, request.Method, null);
        return Results.Json(new { id = result.RequestId }, statusCode: StatusCodes.Status202Accepted);
    }

    private static readonly Action<ILogger, Guid, Guid, string, string, Exception?> LogCaptured =
        LoggerMessage.Define<Guid, Guid, string, string>(
            LogLevel.Information,
            new EventId(8, "RequestCaptured"),
            "Captured {Method} request {RequestId} for endpoint {EndpointId} ({Slug})");

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
