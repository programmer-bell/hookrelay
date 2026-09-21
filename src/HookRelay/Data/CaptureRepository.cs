using Dapper;
using HookRelay.Domain;

namespace HookRelay.Data;

public sealed class CaptureRepository
{
    private readonly Db _db;

    public CaptureRepository(Db db) => _db = db;

    public async Task<CapturedRequest?> FindByIdempotencyKeyAsync(
        Guid endpointId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", endpoint_id AS "EndpointId", method AS "Method",
                   headers AS "Headers", body AS "Body", query AS "Query",
                   idempotency_key AS "IdempotencyKey", received_at AS "ReceivedAt"
            FROM captured_requests
            WHERE endpoint_id = @EndpointId AND idempotency_key = @IdempotencyKey
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CapturedRequest>(sql, new { endpointId, idempotencyKey });
    }

    public async Task<CapturedRequest> CaptureAsync(CapturedRequest request, CancellationToken ct = default)
    {
        const string insertRequest = """
            INSERT INTO captured_requests (id, endpoint_id, method, headers, body, query, idempotency_key, received_at)
            VALUES (@Id, @EndpointId, @Method, @Headers::jsonb, @Body, @Query, @IdempotencyKey, @ReceivedAt)
            """;

        const string insertDelivery = """
            INSERT INTO deliveries (id, request_id, status, attempt_count, next_attempt_at, updated_at)
            VALUES (@Id, @RequestId, 'pending', 0, @NextAttemptAt, @UpdatedAt)
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(insertRequest, request, transaction);

        await connection.ExecuteAsync(insertDelivery, new
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            NextAttemptAt = request.ReceivedAt,
            UpdatedAt = request.ReceivedAt,
        }, transaction);

        await transaction.CommitAsync(ct);
        return request;
    }
}
