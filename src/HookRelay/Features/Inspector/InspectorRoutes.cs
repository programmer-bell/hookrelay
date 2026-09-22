using HookRelay.Data;
using HookRelay.Domain;
using HookRelay.Rendering;
using HookRelay.Services;

namespace HookRelay.Features.Inspector;

public static class InspectorRoutes
{
    private const int RecentRequestLimit = 50;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapInspectorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/endpoints/{slug}/stream", StreamAsync);
        app.MapGet("/endpoints/{slug}/rows", RequestRowsAsync);
        app.MapPost("/deliveries/{id:guid}/replay", ReplayAsync);
    }

    private static ILogger GetLogger(ILoggerFactory factory) =>
        factory.CreateLogger(typeof(InspectorRoutes).FullName ?? nameof(InspectorRoutes));

    private static async Task StreamAsync(
        string slug,
        HttpContext context,
        EndpointRepository endpoints,
        EventBus eventBus,
        IRazorViewRenderer renderer,
        CancellationToken ct)
    {
        var endpoint = await endpoints.GetBySlugAsync(slug, ct);
        if (endpoint is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var requests = eventBus.Subscribe<RequestCapturedEvent>();
        var statuses = eventBus.Subscribe<DeliveryStatusChangedEvent>();
        try
        {
            while (true)
            {
                var readRequests = requests.WaitToReadAsync(ct).AsTask();
                var readStatuses = statuses.WaitToReadAsync(ct).AsTask();
                var heartbeatTask = Task.Delay(HeartbeatInterval, ct);
                var firstToComplete = await Task.WhenAny(readRequests, readStatuses, heartbeatTask);

                if (firstToComplete == readRequests && readRequests.Result)
                {
                    while (requests.TryRead(out var @event))
                    {
                        if (string.Equals(@event.Slug, slug, StringComparison.Ordinal))
                        {
                            var row = new RequestRow(
                                @event.CapturedRequestId,
                                @event.Method,
                                @event.Headers,
                                @event.Body,
                                @event.Query,
                                @event.ReceivedAt,
                                DeliveryStatus.Pending,
                                DeliveryId: null);

                            var html = await renderer.RenderToStringAsync(
                                "~/Views/Partials/_RequestRow.cshtml",
                                row,
                                ct);
                            await WriteFrameAsync(context, "request-row", html, ct);
                        }
                    }

                    await context.Response.Body.FlushAsync(ct);
                }
                else if (firstToComplete == readStatuses && readStatuses.Result)
                {
                    while (statuses.TryRead(out var @event))
                    {
                        if (string.Equals(@event.Slug, slug, StringComparison.Ordinal))
                        {
                            var badgeHtml = await renderer.RenderToStringAsync(
                                "~/Views/Partials/_DeliveryBadge.cshtml",
                                @event.Status,
                                ct);
                            var data = $"<span data-request-id=\"{@event.RequestId}\">{badgeHtml}</span>";
                            await WriteFrameAsync(context, "delivery-status", data, ct);
                        }
                    }

                    await context.Response.Body.FlushAsync(ct);
                }
                else
                {
                    await context.Response.WriteAsync(": heartbeat-sse\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
        }
        finally
        {
            eventBus.Unsubscribe(requests);
            eventBus.Unsubscribe(statuses);
        }
    }

    private static async Task WriteFrameAsync(HttpContext context, string eventName, string data, CancellationToken ct)
    {
        await context.Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
    }

    private static async Task<IResult> RequestRowsAsync(
        string slug,
        string? status,
        EndpointRepository endpoints,
        CaptureRepository captures,
        DeliveryRepository deliveries,
        IRazorViewRenderer renderer,
        CancellationToken ct)
    {
        var endpoint = await endpoints.GetBySlugAsync(slug, ct);
        if (endpoint is null)
        {
            return Results.NotFound();
        }

        var filter = StatusFilter.Normalize(status);
        var statusFilter = filter == StatusFilter.All ? null : filter;
        var requests = await captures.GetRecentAsync(endpoint.Id, RecentRequestLimit, statusFilter, ct);
        var attempts = await deliveries.GetAttemptsAsync(requests.Select(r => r.Id).ToList(), ct);
        var attemptsByRequest = new Dictionary<Guid, IReadOnlyList<DeliveryAttempt>>();
        foreach (var group in attempts.GroupBy(a => a.RequestId))
        {
            attemptsByRequest[group.Key] = group.Select(a => a.Attempt).ToList();
        }

        var rows = requests
            .Select(row => row with
            {
                Attempts = attemptsByRequest.TryGetValue(row.Id, out var rowAttempts) ? rowAttempts : [],
            })
            .ToList();

        var html = await renderer.RenderToStringAsync("~/Views/Partials/_RequestRows.cshtml", rows, ct);
        return Results.Content(html, "text/html");
    }

    private static async Task<IResult> ReplayAsync(
        Guid id,
        DeliveryRepository deliveries,
        EventBus eventBus,
        IRazorViewRenderer renderer,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = GetLogger(loggerFactory);
        var info = await deliveries.ReplayAsync(id, ct);
        if (info is null)
        {
            return Results.NotFound();
        }

        eventBus.Publish(new DeliveryStatusChangedEvent(
            info.DeliveryId,
            info.RequestId,
            info.Slug,
            DeliveryStatus.Pending));
        LogReplayed(logger, info.DeliveryId, info.RequestId, info.Slug, null);

        var badgeHtml = await renderer.RenderToStringAsync(
            "~/Views/Partials/_DeliveryBadge.cshtml",
            DeliveryStatus.Pending,
            ct);
        return Results.Content(badgeHtml, "text/html");
    }

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> LogReplayed =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Information,
            new EventId(7, "ReplayRequested"),
            "Delivery {DeliveryId} replayed for request {RequestId} ({Slug})");
}
