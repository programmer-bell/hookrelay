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

        var reader = eventBus.Subscribe();
        try
        {
            while (true)
            {
                var readTask = reader.WaitToReadAsync(ct).AsTask();
                var heartbeatTask = Task.Delay(HeartbeatInterval, ct);
                var firstToComplete = await Task.WhenAny(readTask, heartbeatTask);

                if (firstToComplete == readTask && readTask.Result)
                {
                    while (reader.TryRead(out var @event))
                    {
                        if (!string.Equals(@event.Slug, slug, StringComparison.Ordinal))
                        {
                            continue;
                        }

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
                        await context.Response.Body.FlushAsync(ct);
                    }
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
            eventBus.Unsubscribe(reader);
        }
    }

    private static async Task WriteFrameAsync(HttpContext context, string eventName, string data, CancellationToken ct)
    {
        await context.Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
    }
}
