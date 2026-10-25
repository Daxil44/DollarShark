using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Transactions;
using DollarShark.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<FinanceRepository>();
builder.Services.AddScoped<IAccountRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ICategoryRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ITransactionRepository>(sp => sp.GetRequiredService<FinanceRepository>());

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

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health").WithTags("Health");

app.Run();

public partial class Program;
