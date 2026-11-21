using Chubb.Claims.Api.Data;
using Chubb.Claims.Api.Workflow;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ClaimsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Claims") ?? "Data Source=claims.db;Foreign Keys=True"));
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.MigrateAsync();
    await DevelopmentSeed.InitializeAsync(db);
}

app.MapClaimsEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
