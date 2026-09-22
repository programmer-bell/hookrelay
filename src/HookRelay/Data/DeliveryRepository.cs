using Dapper;
using HookRelay.Domain;

namespace HookRelay.Data;

public sealed class DeliveryRepository
{
    private readonly Db _db;

    public DeliveryRepository(Db db) => _db = db;

    public async Task<IReadOnlyList<DeliveryJob>> ClaimDueAsync(int batch, CancellationToken ct = default)
    {
        const string claimSql = """
            UPDATE deliveries d
            SET status = 'delivering', updated_at = now()
            WHERE d.id IN (
                SELECT id FROM deliveries
                WHERE status = 'pending' AND next_attempt_at <= now()
                ORDER BY next_attempt_at
                LIMIT @Batch
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "DeliveryId", request_id AS "RequestId",
                      attempt_count AS "AttemptCount"
            """;

        const string detailsSql = """
            SELECT d.id AS "DeliveryId", d.request_id AS "RequestId",
                   e.slug AS "Slug", e.signing_secret AS "SigningSecret",
                   e.target_url AS "TargetUrl", c.headers AS "Headers",
                   c.body AS "Body", d.attempt_count AS "AttemptCount"
            FROM deliveries d
            JOIN captured_requests c ON c.id = d.request_id
            JOIN endpoints e ON e.id = c.endpoint_id
            WHERE d.id = ANY(@Ids)
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);

        var claimed = await connection.QueryAsync<DeliveryClaim>(claimSql, new { batch });
        var ids = claimed.Select(c => c.DeliveryId).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var jobs = await connection.QueryAsync<DeliveryJob>(detailsSql, new { ids });
        return jobs.ToList();
    }

    public async Task RecordAttemptAsync(
        Guid deliveryId,
        int? statusCode,
        string? error,
        int durationMs,
        DateTime attemptedAt,
        CancellationToken ct = default)
    {
        const string insertAttempt = """
            INSERT INTO delivery_attempts (id, delivery_id, attempted_at, status_code, error, duration_ms)
            VALUES (@Id, @DeliveryId, @AttemptedAt, @StatusCode, @Error, @DurationMs)
            """;

        const string updateDelivery = """
            UPDATE deliveries
            SET attempt_count = attempt_count + 1,
                last_status_code = @StatusCode,
                last_error = @Error,
                updated_at = @UpdatedAt
            WHERE id = @DeliveryId
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(insertAttempt, new
        {
            Id = Guid.NewGuid(),
            deliveryId,
            attemptedAt,
            statusCode,
            error,
            durationMs,
        }, transaction);

        await connection.ExecuteAsync(updateDelivery, new { deliveryId, statusCode, error, updatedAt = attemptedAt }, transaction);

        await transaction.CommitAsync(ct);
    }

    public async Task MarkSucceededAsync(Guid deliveryId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE deliveries
            SET status = 'succeeded', next_attempt_at = NULL, updated_at = now()
            WHERE id = @DeliveryId
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(sql, new { deliveryId });
    }

    public async Task MarkRetryAsync(Guid deliveryId, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE deliveries
            SET status = 'pending', next_attempt_at = @NextAttemptAt, updated_at = now()
            WHERE id = @DeliveryId
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(sql, new { deliveryId, nextAttemptAt });
    }

    public async Task MarkDeadAsync(Guid deliveryId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE deliveries
            SET status = 'dead', next_attempt_at = NULL, updated_at = now()
            WHERE id = @DeliveryId
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(sql, new { deliveryId });
    }

    public async Task<int> RecoverStuckAsync(CancellationToken ct = default)
    {
        const string sql = """
            UPDATE deliveries
            SET status = 'pending', next_attempt_at = now(), updated_at = now()
            WHERE status = 'delivering' AND updated_at <= now() - interval '5 minutes'
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(sql);
    }

    public async Task<DeliveryReplayInfo?> ReplayAsync(Guid deliveryId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE deliveries d
            SET status = 'pending', attempt_count = 0, next_attempt_at = now(),
                last_status_code = NULL, last_error = NULL, updated_at = now()
            FROM captured_requests c, endpoints e
            WHERE d.id = @DeliveryId
              AND d.status = 'dead'
              AND c.id = d.request_id
              AND e.id = c.endpoint_id
            RETURNING d.id AS "DeliveryId", d.request_id AS "RequestId", e.slug AS "Slug"
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<DeliveryReplayInfo>(sql, new { deliveryId });
    }

    public async Task<IReadOnlyList<RequestAttempt>> GetAttemptsAsync(
        IReadOnlyList<Guid> requestIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT d.request_id AS "RequestId",
                   a.status_code AS "StatusCode", a.duration_ms AS "DurationMs",
                   a.error AS "Error", a.attempted_at AS "AttemptedAt"
            FROM delivery_attempts a
            JOIN deliveries d ON d.id = a.delivery_id
            WHERE d.request_id = ANY(@RequestIds)
            ORDER BY a.attempted_at
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<DeliveryAttemptRow>(sql, new { requestIds = requestIds.ToArray() });
        return rows.Select(r => new RequestAttempt(r.RequestId, new DeliveryAttempt(r.StatusCode, r.DurationMs, r.Error, r.AttemptedAt)))
                   .ToList();
    }

    public sealed record DeliveryReplayInfo(Guid DeliveryId, Guid RequestId, string Slug);

    private sealed record DeliveryClaim(Guid DeliveryId, Guid RequestId, int AttemptCount);

    private sealed record DeliveryAttemptRow(
        Guid RequestId,
        int? StatusCode,
        int? DurationMs,
        string? Error,
        DateTime AttemptedAt);
}
