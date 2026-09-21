using Dapper;
using Endpoint = HookRelay.Domain.Endpoint;

namespace HookRelay.Data;

public sealed class EndpointRepository
{
    private readonly Db _db;

    public EndpointRepository(Db db) => _db = db;

    public async Task<Endpoint> CreateAsync(Endpoint endpoint, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO endpoints (id, slug, name, target_url, signing_secret)
            VALUES (@Id, @Slug, @Name, @TargetUrl, @SigningSecret)
            RETURNING id AS "Id", slug AS "Slug", name AS "Name",
                      target_url AS "TargetUrl", signing_secret AS "SigningSecret",
                      created_at AS "CreatedAt"
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        return await connection.QuerySingleAsync<Endpoint>(sql, endpoint);
    }

    public async Task<Endpoint?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", slug AS "Slug", name AS "Name",
                   target_url AS "TargetUrl", signing_secret AS "SigningSecret",
                   created_at AS "CreatedAt"
            FROM endpoints WHERE slug = @Slug
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Endpoint>(sql, new { slug });
    }

    public async Task<IReadOnlyList<Endpoint>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", slug AS "Slug", name AS "Name",
                   target_url AS "TargetUrl", signing_secret AS "SigningSecret",
                   created_at AS "CreatedAt"
            FROM endpoints ORDER BY created_at DESC
            """;

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<Endpoint>(sql);
        return rows.ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM endpoints WHERE id = @Id";

        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(sql, new { id });
    }
}
