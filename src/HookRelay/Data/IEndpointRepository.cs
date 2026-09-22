using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Data;

public interface IEndpointRepository
{
    Task<Endpoint?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
