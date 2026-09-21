using HookRelay.Data;
using HookRelay.Features.Inspector;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Views.Pages;

public class EndpointModel : PageModel
{
    private const int RecentRequestLimit = 50;

    private readonly EndpointRepository _endpoints;
    private readonly CaptureRepository _captures;

    public EndpointModel(EndpointRepository endpoints, CaptureRepository captures)
    {
        _endpoints = endpoints;
        _captures = captures;
    }

    [FromRoute]
    public string? Slug { get; set; }

    public Endpoint? Endpoint { get; private set; }

    public IReadOnlyList<RequestRow> Requests { get; private set; } = [];

    public string IngestUrl { get; private set; } = string.Empty;

    public string StreamUrl { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Slug))
        {
            return NotFound();
        }

        var endpoint = await _endpoints.GetBySlugAsync(Slug, ct);
        if (endpoint is null)
        {
            return NotFound();
        }

        Endpoint = endpoint;
        IngestUrl = $"{Request.Scheme}://{Request.Host}/h/{endpoint.Slug}";
        StreamUrl = $"/endpoints/{endpoint.Slug}/stream";
        Requests = (IReadOnlyList<RequestRow>)await _captures.GetRecentAsync(endpoint.Id, RecentRequestLimit, ct);
        return Page();
    }
}
