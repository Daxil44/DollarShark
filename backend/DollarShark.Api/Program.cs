using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Application.Dashboard;
using DollarShark.Application.Transactions;
using DollarShark.Application.Transfers;
using DollarShark.Application.Statements;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using DollarShark.Api.Controllers;
using DollarShark.Api.Email;
using DollarShark.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddScoped<ICategoryRuleService, CategoryRuleService>();
builder.Services.AddScoped<ICategoryRuleRepository, CategoryRuleRepository>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<FinanceRepository>();
builder.Services.AddScoped<IAccountRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ICategoryRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ITransactionRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<ITransferRepository>(sp => sp.GetRequiredService<FinanceRepository>());
builder.Services.AddScoped<IStatementImportService, StatementImportService>();
builder.Services.AddScoped<IStatementImportRepository, StatementImportRepository>();
var duplicateDetectionOptions = builder.Configuration.GetSection("DuplicateDetection").Get<DuplicateDetectionOptions>() ?? new DuplicateDetectionOptions();
if (duplicateDetectionOptions.DateToleranceDays is < 0 or > 30 ||
    duplicateDetectionOptions.SimilarDescriptionDateToleranceDays < 0 ||
    duplicateDetectionOptions.SimilarDescriptionDateToleranceDays > duplicateDetectionOptions.DateToleranceDays)
    throw new InvalidOperationException("DuplicateDetection date tolerances must be between 0 and 30 days, and similar-description tolerance cannot exceed the overall tolerance.");
builder.Services.AddSingleton(duplicateDetectionOptions);
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

// Optional "Continue with Google". Enabled only when both values are configured (User Secrets / environment).
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Google returns with a top-level GET redirect, so Lax is enough and also works on http://localhost.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ClaimActions.MapJsonKey(AuthController.GoogleEmailVerifiedClaim, "verified_email");
        options.ClaimActions.MapJsonKey(AuthController.GoogleEmailVerifiedClaim, "email_verified");
    });
}

// Account emails (confirmation + password reset).
builder.Services.Configure<AccountAuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.PostConfigure<AccountAuthOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["Auth:FrontendBaseUrl"]))
        options.FrontendBaseUrl = parsedFrontendOrigin.GetLeftPart(UriPartial.Authority);
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(3));
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
if (string.IsNullOrWhiteSpace(builder.Configuration["Email:Provider"]) && !string.IsNullOrWhiteSpace(emailOptions.Smtp.Host))
    emailOptions.Provider = EmailOptions.SmtpProvider;
if (string.Equals(emailOptions.Provider, EmailOptions.SmtpProvider, StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(emailOptions.Smtp.Host) || emailOptions.Smtp.Port is < 1 or > 65535 ||
        !System.Net.Mail.MailAddress.TryCreate(emailOptions.FromAddress, out _))
        throw new InvalidOperationException("Email:Provider is Smtp, so Email:Smtp:Host, a valid Email:Smtp:Port and Email:FromAddress are required.");
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else if (string.Equals(emailOptions.Provider, EmailOptions.LogProvider, StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("The Log email provider writes account links to the log and is only allowed in Development. Configure Email:Provider=Smtp with Email:Smtp settings.");
    builder.Services.AddScoped<IEmailSender, LogEmailSender>();
}
else
{
    throw new InvalidOperationException("Email:Provider must be Smtp or Log.");
}
builder.Services.Configure<EmailOptions>(options =>
{
    builder.Configuration.GetSection("Email").Bind(options);
    options.Provider = emailOptions.Provider;
});
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddHostedService<EmailDispatchService>();

// Limit endpoints that send email so they cannot be used to flood an inbox or exhaust the mail quota.
var emailPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthEmail:PermitLimit", 5);
var emailWindowMinutes = builder.Configuration.GetValue("RateLimiting:AuthEmail:WindowMinutes", 15);
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
var loginWindowMinutes = builder.Configuration.GetValue("RateLimiting:Login:WindowMinutes", 5);
var uploadPermitLimit = builder.Configuration.GetValue("RateLimiting:Upload:PermitLimit", 30);
var uploadWindowMinutes = builder.Configuration.GetValue("RateLimiting:Upload:WindowMinutes", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.EmailRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, emailPermitLimit),
            Window = TimeSpan.FromMinutes(Math.Max(1, emailWindowMinutes)),
            QueueLimit = 0,
        }));
    // Sign-in attempts per IP. Account lockout already protects one account from repeated guesses;
    // this stops one source from guessing across many accounts (credential stuffing / password spraying)
    // and from burning CPU on password hashing. A sliding window avoids a burst right at each window reset.
    options.AddPolicy(AuthController.LoginRateLimitPolicy, context => RateLimitPartition.GetSlidingWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, loginPermitLimit),
            Window = TimeSpan.FromMinutes(Math.Max(1, loginWindowMinutes)),
            SegmentsPerWindow = Math.Max(1, loginWindowMinutes),
            QueueLimit = 0,
        }));
    // Statement uploads per signed-in user (IP as a fallback): stops a script filling the disk with files.
    options.AddPolicy(StatementsController.UploadRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } userId
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, uploadPermitLimit),
            Window = TimeSpan.FromMinutes(Math.Max(1, uploadWindowMinutes)),
            QueueLimit = 0,
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1);
        var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = $"Too many requests. Please wait about {minutes} minute{(minutes == 1 ? "" : "s")} and try again.",
        }, cancellationToken);
    };
});

// ---- Production hosting ----

// Behind Nginx / a load balancer the API sees the proxy's address. Trust X-Forwarded-For/Proto only from
// configured proxies so rate limits use the real visitor IP and generated links use https.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

// Login cookies, CSRF tokens and email links are encrypted with data-protection keys. Keys kept only in memory
// would log everyone out (and break pending email links) on every restart or container redeploy.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("DollarShark");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
else if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Set DataProtection:KeysPath to a private, persistent directory (for example a Docker volume) so sign-ins survive restarts.");

var redirectToHttps = builder.Configuration.GetValue("Https:Redirect", !builder.Environment.IsDevelopment());
builder.Services.AddHttpsRedirection(options => options.HttpsPort = builder.Configuration.GetValue("Https:Port", 443));
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(builder.Configuration.GetValue("Https:HstsMaxAgeDays", 180));
    options.IncludeSubDomains = false;
});

// Unhandled errors become a plain ProblemDetails response: no stack traces or exception messages reach the
// browser, and the traceId lets you find the full error in the server log.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    if (context.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
    {
        context.ProblemDetails.Title = "Something went wrong on our side. Please try again.";
        context.ProblemDetails.Detail = null;
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}
if (redirectToHttps) app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// After authentication so per-user limits (statement uploads) can see who is signed in.
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health").WithTags("Health");

app.Run();

public partial class Program;
