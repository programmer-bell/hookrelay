using HookRelay.Config;
using HookRelay.Data;
using HookRelay.Features.Endpoints;
using HookRelay.Features.Health;
using HookRelay.Rendering;
using HookRelay.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(options => options.RootDirectory = "/Views/Pages");

builder.Services.AddSingleton(services =>
    Db.Open(builder.Configuration, services.GetRequiredService<ILogger<Db>>()));
builder.Services.AddSingleton<EndpointRepository>();
builder.Services.AddSingleton<TargetUrlValidator>();
builder.Services.AddSingleton<IRazorViewRenderer, RazorViewRenderer>();

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
var migrator = new Migrator(
    db,
    app.Services.GetRequiredService<ILogger<Migrator>>(),
    Path.Combine(app.Environment.ContentRootPath, "Migrations"));
await migrator.MigrateAsync();

app.UseStaticFiles();

app.MapRazorPages();
app.MapHealthEndpoints();
app.MapEndpointRoutes();

app.Run();
