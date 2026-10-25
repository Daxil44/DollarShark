using DollarShark.Application.Accounts;
using DollarShark.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddScoped<IAccountReader, AccountReader>();
builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FinanceDb"),
        sql => sql.MigrationsAssembly("DollarShark.Infrastructure")));

if (builder.Environment.IsDevelopment())
{
    var viteOrigin = builder.Configuration["ViteOrigin"] ?? "http://localhost:5173";
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(viteOrigin).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseCors();
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health").WithTags("Health");
app.MapGet("/api/accounts", async (IAccountReader reader, CancellationToken cancellationToken) =>
    Results.Ok(await reader.GetAccountsAsync(cancellationToken)))
    .WithName("GetAccounts").WithTags("Accounts");
app.Run();

public partial class Program;
