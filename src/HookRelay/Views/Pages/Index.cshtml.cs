using HookRelay.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Views.Pages;

public class IndexModel : PageModel
{
    private readonly EndpointRepository _endpoints;

    public IndexModel(EndpointRepository endpoints) => _endpoints = endpoints;

    public IReadOnlyList<Endpoint> Endpoints { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Endpoints = (IReadOnlyList<Endpoint>)await _endpoints.ListAsync(ct);
    }
}
