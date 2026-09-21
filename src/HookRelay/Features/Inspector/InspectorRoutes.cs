using HookRelay.Data;
using HookRelay.Domain;
using HookRelay.Rendering;
using HookRelay.Services;

namespace HookRelay.Features.Inspector;

public static class InspectorRoutes
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static void MapInspectorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/endpoints/{slug}/stream", StreamAsync);
    }

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
                                DeliveryStatus.Pending);

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
}
