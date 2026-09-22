using HookRelay.Domain;

namespace HookRelay.Data;

public interface ICaptureRepository
{
    Task<CapturedRequest?> FindByIdempotencyKeyAsync(Guid endpointId, string idempotencyKey, CancellationToken ct = default);

    Task<CapturedRequest> CaptureAsync(CapturedRequest request, CancellationToken ct = default);
}
