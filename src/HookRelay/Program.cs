using System.Globalization;
using System.Threading.RateLimiting;
using HookRelay.Config;
using HookRelay.Data;
using HookRelay.Features.Endpoints;
using HookRelay.Features.Health;
using HookRelay.Features.Ingest;
using HookRelay.Middleware;
using HookRelay.Rendering;
using HookRelay.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(options => options.RootDirectory = "/Views/Pages");

builder.Services.AddSingleton(services =>
    Db.Open(builder.Configuration, services.GetRequiredService<ILogger<Db>>()));
builder.Services.AddSingleton<EndpointRepository>();
builder.Services.AddSingleton<CaptureRepository>();
builder.Services.AddSingleton<TargetUrlValidator>();
builder.Services.AddSingleton<IRazorViewRenderer, RazorViewRenderer>();
builder.Services.AddSingleton<EventBus>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        await context.HttpContext.Response.WriteAsync("Too many requests.", ct);
    };
    options.AddPolicy("ingest", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
var migrator = new Migrator(
    db,
    app.Services.GetRequiredService<ILogger<Migrator>>(),
    Path.Combine(app.Environment.ContentRootPath, "Migrations"));
await migrator.MigrateAsync();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHealthEndpoints();
app.MapEndpointRoutes();
app.MapIngestEndpoints();

app.Run();

static string GetPartitionKey(HttpContext httpContext)
{
    var segments = (httpContext.Request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
    return segments.Length == 2 && string.Equals(segments[0], "h", StringComparison.OrdinalIgnoreCase)
        ? segments[1]
        : "unknown";
}
