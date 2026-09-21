using HookRelay.Config;
using HookRelay.Data;
using HookRelay.Features.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

builder.Services.AddSingleton(services =>
    Db.Open(builder.Configuration, services.GetRequiredService<ILogger<Db>>()));
builder.Services.AddSingleton<EndpointRepository>();

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
var migrator = new Migrator(
    db,
    app.Services.GetRequiredService<ILogger<Migrator>>(),
    Path.Combine(app.Environment.ContentRootPath, "Migrations"));
await migrator.MigrateAsync();

app.MapHealthEndpoints();

app.Run();
