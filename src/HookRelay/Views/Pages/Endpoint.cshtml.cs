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
    private readonly DeliveryRepository _deliveries;

    public EndpointModel(
        EndpointRepository endpoints,
        CaptureRepository captures,
        DeliveryRepository deliveries)
    {
        _endpoints = endpoints;
        _captures = captures;
        _deliveries = deliveries;
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

        var requests = await _captures.GetRecentAsync(endpoint.Id, RecentRequestLimit, ct);
        var attemptsByRequest = await LoadAttemptsAsync(requests, ct);

        Requests = requests
            .Select(row => row with
            {
                Attempts = attemptsByRequest.TryGetValue(row.Id, out var attempts) ? attempts : [],
            })
            .ToList();

        return Page();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<HookRelay.Domain.DeliveryAttempt>>> LoadAttemptsAsync(
        IReadOnlyList<RequestRow> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<HookRelay.Domain.DeliveryAttempt>>();
        }

        var attempts = await _deliveries.GetAttemptsAsync(
            requests.Select(r => r.Id).ToList(),
            ct);

        return attempts
            .GroupBy(a => a.RequestId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<HookRelay.Domain.DeliveryAttempt>)g.Select(a => a.Attempt).ToList());
    }
}
