using System.Diagnostics;
using HookRelay.Data;
using HookRelay.Rendering;
using HookRelay.Services;
using Npgsql;
using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Features.Endpoints;

public static class EndpointRoutes
{
    public static void MapEndpointRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/endpoints", HandleCreateAsync);
        app.MapDelete("/endpoints/{id:guid}", HandleDeleteAsync);
    }

    private static async Task<IResult> HandleCreateAsync(
        HttpRequest request,
        EndpointRepository endpoints,
        TargetUrlValidator validator,
        IRazorViewRenderer views,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var name = form["name"].ToString().Trim();
        var targetUrl = form["target_url"].ToString().Trim();

        var errors = new List<string>();
        if (name.Length == 0)
        {
            errors.Add("Name is required.");
        }

        var urlResult = await validator.ValidateAsync(targetUrl, ct);
        errors.AddRange(urlResult.Errors);

        if (errors.Count > 0)
        {
            var model = new EndpointFormModel(name, targetUrl, errors);
            var formHtml = await views.RenderToStringAsync("~/Views/Partials/_EndpointForm.cshtml", model, ct);
            return Results.Content(formHtml, "text/html", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await CreateEndpointWithRetryAsync(endpoints, name, targetUrl, ct);

        var list = await endpoints.ListAsync(ct);
        var listHtml = await views.RenderToStringAsync("~/Views/Partials/_EndpointList.cshtml", list, ct);
        return Results.Content(listHtml, "text/html");
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        EndpointRepository endpoints,
        IRazorViewRenderer views,
        CancellationToken ct)
    {
        await endpoints.DeleteAsync(id, ct);

        var list = await endpoints.ListAsync(ct);
        var listHtml = await views.RenderToStringAsync("~/Views/Partials/_EndpointList.cshtml", list, ct);
        return Results.Content(listHtml, "text/html");
    }

    private static async Task<Endpoint> CreateEndpointWithRetryAsync(
        EndpointRepository endpoints,
        string name,
        string targetUrl,
        CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var endpoint = new Endpoint(
                Guid.NewGuid(),
                EndpointTokens.NewSlug(),
                name,
                targetUrl,
                EndpointTokens.NewSigningSecret(),
                TimeProvider.System.GetUtcNow().UtcDateTime);

            try
            {
                return await endpoints.CreateAsync(endpoint, ct);
            }
            catch (PostgresException ex) when (attempt < maxAttempts && ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Slug collisions are vanishingly rare; retry with a fresh slug.
            }
        }

        throw new UnreachableException();
    }
}
