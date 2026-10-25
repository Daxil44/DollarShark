using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Application.Dashboard;
using DollarShark.Application.Transactions;
using DollarShark.Application.Statements;
using DollarShark.Api.Security;
using DollarShark.Infrastructure.Identity;
using DollarShark.Infrastructure.Persistence;
using DollarShark.Infrastructure.Statements;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddScoped<ApiCsrfAuthorizationFilter>();
builder.Services.AddControllers(options => options.Filters.AddService<ApiCsrfAuthorizationFilter>());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<FinanceRepository>();
builder.Services.AddScoped<IAccountRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ICategoryRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ITransactionRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<IStatementImportService, StatementImportService>();
builder.Services.AddScoped<IStatementImportRepository, StatementImportRepository>();
builder.Services.AddScoped<IStatementFileValidator, StatementFileValidator>();
builder.Services.AddScoped<IStatementCsvParser, StatementCsvParser>();
builder.Services.AddScoped<IStatementStorage, LocalPrivateStatementStorage>();

var maxStatementSizeBytes = builder.Configuration.GetValue<long>("StatementStorage:MaxFileSizeBytes", 15 * 1024 * 1024);
if (maxStatementSizeBytes is < 1 or > 1024L * 1024 * 1024)
    throw new InvalidOperationException("StatementStorage:MaxFileSizeBytes must be between 1 byte and 1 GiB.");
builder.Services.AddSingleton(new StatementUploadOptions { MaxFileSizeBytes = maxStatementSizeBytes });
var maxStatementCsvRows = builder.Configuration.GetValue<int>("StatementCsv:MaxRows", 10_000);
if (maxStatementCsvRows is < 1 or > 100_000)
    throw new InvalidOperationException("StatementCsv:MaxRows must be between 1 and 100000.");
builder.Services.AddSingleton(new StatementCsvParsingOptions { MaxRows = maxStatementCsvRows });
builder.Services.Configure<StatementStorageOptions>(builder.Configuration.GetSection("StatementStorage"));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxStatementSizeBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxStatementSizeBytes + 1024 * 1024);

builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FinanceDb"),
        sql => sql.MigrationsAssembly("DollarShark.Infrastructure")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<FinanceDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "DollarShark.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "DollarShark.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var allowedFrontendOrigin = builder.Environment.IsDevelopment()
    ? builder.Configuration["ViteOrigin"] ?? "http://localhost:5173"
    : builder.Configuration["FrontendOrigin"];
if (string.IsNullOrWhiteSpace(allowedFrontendOrigin) || !Uri.TryCreate(allowedFrontendOrigin, UriKind.Absolute, out var parsedFrontendOrigin))
    throw new InvalidOperationException("Configure ViteOrigin for development or FrontendOrigin for production as one exact absolute URL.");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(parsedFrontendOrigin.GetLeftPart(UriPartial.Authority)).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health").WithTags("Health");

app.Run();

public partial class Program;
