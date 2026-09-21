using Dapper;
using HookRelay.Data;

namespace HookRelay.Features.Health;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok("Healthy"));

        app.MapGet("/readyz", async (Db db, CancellationToken ct) =>
        {
            try
            {
                await using var connection = await db.DataSource.OpenConnectionAsync(ct);
                await connection.ExecuteScalarAsync<int>("SELECT 1");
                return Results.Ok("Ready");
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}
