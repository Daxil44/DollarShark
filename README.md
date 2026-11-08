# DollarShark

DollarShark is a personal finance app with email/password accounts, cookie-based sessions, CSRF protection, and per-user isolation for financial records. Each user sees their own accounts, transactions, and private categories; read-only default categories are shared.

## Prerequisites

- .NET 10 SDK
- Node.js 20.19 or newer and npm (Node 22 LTS or newer recommended)
- SQL Server Developer Edition running locally

## Database setup and existing data

Set the SQL Server connection string in .NET User Secrets. Replace YOUR_SERVER\YOUR_INSTANCE with the server/instance shown by your SQL Server connection:

~~~powershell
dotnet user-secrets set "ConnectionStrings:FinanceDb" "Server=YOUR_SERVER\YOUR_INSTANCE;Database=DollarShark;Trusted_Connection=True;TrustServerCertificate=True;" --project backend/DollarShark.Api
~~~

Apply the checked-in migrations explicitly. The migrations preserve existing tables and rows. Phase 2.5 adds ASP.NET Core Identity tables and nullable owner columns; existing financial rows stay unassigned and are hidden from signed-in users until deliberately backfilled. No user is selected automatically.

~~~powershell
.\.tools\dotnet-ef.exe database update --project backend/DollarShark.Infrastructure --startup-project backend/DollarShark.Api
~~~

The API does not update the database automatically. After migration, start the app and register the intended initial user. If the database already has Phase 1/2 finance records, open `docs/phase-2-5-backfill.sql` in VS Code with the MSSQL extension, replace the placeholder email, select the intended SQL Server connection/database, and run it. The script changes only rows whose `UserId` is null, leaves shared default categories unowned, and reports how many legacy records it assigned. Inspect the email and selected database before running the script. A fresh database has no legacy finance rows, so no backfill is needed.

Phase 3.1 adds a `StatementImports` table through a new migration. It does not modify previous migrations or migrate the SQL Server database automatically.

## Run from Visual Studio

Open backend/DollarShark.sln, select the DollarShark Full Stack launch profile, and press F5. The API listens on http://localhost:5017. Its configured SPA proxy starts Vite on http://localhost:5173. Open that Vite URL in your browser. Run npm install in frontend once if dependencies are not installed yet. Register an account or sign in; finance pages redirect anonymous users to login.

## Run from a terminal

From the repository root, run the API using its Development launch profile. The SPA proxy starts the Vite development server, so do not start another npm run dev process at the same time.

~~~powershell
dotnet run --project backend/DollarShark.Api
~~~

Open http://localhost:5173. The checked-in development environment file points the frontend at http://localhost:5017.

## Authentication, cookies, and CSRF

- `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`, and `GET /api/auth/csrf`.
- Account email endpoints: `POST /api/auth/confirm-email`, `POST /api/auth/resend-confirmation`, `POST /api/auth/forgot-password`, and `POST /api/auth/reset-password`.
- Registration creates the account and emails a confirmation link; it does not sign the user in. Sign-in is refused (403, `code: email_not_confirmed`) until the email is confirmed, and that response is only returned after a correct password. Identity stores a salted password hash; the API never returns the password or hash.
- Authentication uses an HttpOnly cookie (8-hour lifetime, sliding expiration, SameSite=Lax). HTTPS requests receive Secure cookies; local HTTP development does not.
- Before any POST, PUT, PATCH, or DELETE, the frontend obtains an antiforgery request token from `/api/auth/csrf` and sends it in `X-CSRF-TOKEN`. The request token stays in memory, never localStorage/sessionStorage.
- Development CORS permits only `http://localhost:5173` by default (override with `ViteOrigin`). Production must set `FrontendOrigin` to the exact HTTPS frontend origin.
- The React dev URL and API use different ports on `localhost`, which remain same-site for SameSite=Lax cookies. For production, use one site or same-site subdomains behind HTTPS. A separate cross-site frontend needs a deliberate `SameSite=None; Secure` deployment configuration and corresponding CSRF/CORS review.

## Account emails (confirmation and password reset)

- Confirmation and reset links point at the frontend (`/confirm-email`, `/reset-password`) and expire after 3 hours. Reset links are single-use, a successful reset also confirms the email and clears any lockout, and links carry the user ID rather than the email address.
- Forgot-password and resend-confirmation always return the same response, so they cannot be used to discover registered emails. Emails are queued and sent in the background.
- Register, resend, forgot-password, and reset-password are rate limited per IP (default 5 requests per 15 minutes; `RateLimiting:AuthEmail:PermitLimit` and `RateLimiting:AuthEmail:WindowMinutes`).
- Sign-in (`POST /api/auth/login`) is rate limited per IP with a sliding window (default 10 attempts per 5 minutes; `RateLimiting:Login:PermitLimit` and `RateLimiting:Login:WindowMinutes`). This complements account lockout (5 wrong passwords locks that account for 15 minutes): lockout protects one account, the IP limit stops one source guessing across many accounts. Rejected requests get `429 Too Many Requests` with a `Retry-After` header and a message saying how long to wait.
- Limits are keyed by the client IP the API sees. Behind a reverse proxy or load balancer, configure forwarded headers first; otherwise every user shares the proxy's IP and therefore one limit.
- **Development default:** with no SMTP settings, emails are not sent. They are written to the API log as `DEVELOPMENT EMAIL (not sent)` including the link, so you can copy it. This `Log` provider is refused outside Development.
- Existing accounts: the `Phase7ConfirmExistingUserEmails` migration marks accounts created before this feature as confirmed so nobody is locked out. Apply it with the usual `database update` command.
- `Auth:RequireConfirmedEmail` (default `true`) can be set to `false` for a private single-user install without email; registration then signs in immediately as before.

### Sending real email with Gmail

1. Turn on 2-Step Verification for the Google account, then create an App Password at https://myaccount.google.com/apppasswords. Your normal Gmail password will not work.
2. Store the settings in User Secrets (never in appsettings files):

~~~powershell
dotnet user-secrets set "Email:Provider" "Smtp" --project backend/DollarShark.Api
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project backend/DollarShark.Api
dotnet user-secrets set "Email:Smtp:Port" "587" --project backend/DollarShark.Api
dotnet user-secrets set "Email:Smtp:Username" "you@gmail.com" --project backend/DollarShark.Api
dotnet user-secrets set "Email:Smtp:Password" "your-16-character-app-password" --project backend/DollarShark.Api
dotnet user-secrets set "Email:FromAddress" "you@gmail.com" --project backend/DollarShark.Api
~~~

Gmail only sends as the signed-in address (or a verified alias), and personal accounts are limited to roughly 500 emails per day, which is fine for testing and small launches. For a public launch, the same SMTP settings work with a transactional provider (Amazon SES, SendGrid, Postmark, Mailgun) and your own domain. In production set these as environment variables (for example `Email__Smtp__Password`) and set `Auth:FrontendBaseUrl` if links should point somewhere other than `FrontendOrigin`.

## Continue with Google (optional)

The "Continue with Google" button only appears when a Google Client ID and Secret are configured; without them the app behaves exactly as before.

1. Go to https://console.cloud.google.com/, create a project (for example "DollarShark").
2. **APIs & Services → OAuth consent screen**: choose *External*, enter the app name, support email and developer email. The default scopes (`openid`, `email`, `profile`) are enough and need no Google review. While the app is in *Testing* mode, add your own Google account under *Test users*.
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**: type *Web application*. Under *Authorized redirect URIs* add `http://localhost:5017/signin-google` (and later `https://<your-api-domain>/signin-google` for production).
4. Store the values in User Secrets:

~~~powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>.apps.googleusercontent.com" --project backend/DollarShark.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>" --project backend/DollarShark.Api
~~~

Restart the API. How accounts are matched:
- First Google sign-in with a new email creates a confirmed account with no password (the user can always use Google, or use *Forgot password?* to add one).
- If a **confirmed** account already has that email, Google is linked to it.
- If an **unconfirmed** account has that email, its password is removed before linking, because whoever registered it never proved they own the address. This blocks "pre-registration" account takeovers.
- Google accounts whose email Google has not verified are refused.

## Production configuration

Settings the API needs outside Development (environment variables use `__` for `:`, e.g. `DataProtection__KeysPath`):

| Setting | What it does |
|---|---|
| `FrontendOrigin` | Exact HTTPS origin of the site (CORS + email links). Required. |
| `DataProtection:KeysPath` | Private, persistent folder (e.g. a Docker volume) for the keys that encrypt login cookies, CSRF tokens and email links. Required in production; without it every restart would log everyone out. Keep it private and backed up. |
| `ReverseProxy:KnownProxies` / `ReverseProxy:KnownNetworks` | IPs or CIDR ranges of Nginx/the load balancer (e.g. the Docker network `172.16.0.0/12`). Only these may set `X-Forwarded-For`/`X-Forwarded-Proto`, so rate limits see the real visitor IP and links use https. Loopback is trusted by default. |
| `Https:Redirect` (default `true` outside Development), `Https:Port` (443), `Https:HstsMaxAgeDays` (180) | Redirect http → https and send HSTS. With TLS terminated at Nginx, keep forwarding `X-Forwarded-Proto` so redirects don't loop. |
| `Email:*` | SMTP settings (see Account emails). The log provider is refused in production. |
| `RateLimiting:Upload:PermitLimit` / `WindowMinutes` | Statement uploads per signed-in user (default 30 per 60 minutes). |

Unhandled server errors return a ProblemDetails body with a friendly title and a `traceId` — never a stack trace or exception message; the full error is in the server log under that trace id. In the browser, an error boundary around each page (and the dashboard content) shows a "this section could not be displayed" card with *Try again* instead of a blank screen.

## Statement uploads

- `POST /api/statements` accepts multipart `financialAccountId` and `file` fields. `GET /api/statements?page=1&pageSize=25`, `GET /api/statements/{id}`, and `DELETE /api/statements/{id}` manage the signed-in user's metadata and files. There is no download endpoint.
- PDF, CSV, and XLSX files are checked by extension and basic file structure; the submitted Content-Type is ignored. The default maximum is 15 MiB (`StatementStorage:MaxFileSizeBytes` can override it).
- Files are stored outside `wwwroot` under the current user's Local Application Data at `DollarShark/Statements`. Set `StatementStorage:RootPath` to choose another private directory. The API refuses a configured storage location inside the public web root.
- The app generates random storage keys and stores a SHA-256 hash plus metadata in SQL Server. Failed metadata writes attempt to remove the stored file. Deletes use a retryable `Deleting` status so a failed file or database deletion can be retried.
- Uploads are not parsed automatically. CSV files can be preview-parsed on demand; previews do not create transactions. Files are never served by the API.
- Local storage is private to the operating-system account running the API, but files are not encrypted or malware-scanned by this milestone. Configure restrictive filesystem permissions, backups, and a trusted private volume before using real statements.

## CSV preview parsing

- `POST /api/statements/{id}/parse-csv` returns normalized candidate rows for an owned CSV statement. It is read-only: previews do not create transactions or change the statement status, and concurrent/repeated previews are safe.
- The parser supports UTF-8 with or without BOM, quoted commas/escaped quotes, CRLF/LF, signed amounts or separate debit/credit columns. It recognizes common header aliases, but unknown institution-specific headers require a future mapping configuration; ambiguous mappings return a clear 422 error.
- Date fields use explicit invariant formats (`yyyy-MM-dd`, `yyyy/MM/dd`, US `M/d/yyyy` and `M/d/yy`, `yyyyMMdd`, `d-MMM-yyyy`, `MMM d, yyyy`, or `MMMM d, yyyy`). Amounts use `.` as decimal separator, optional correctly grouped `,` thousands separators, up to four fractional digits, optional currency symbols, and parentheses or a leading sign for negatives. Debit values are normalized negative and credits positive; signed Amount values keep their sign.
- Optional `StatementCsv:MaxRows` defaults to 10,000 (permitted configuration range 1–100,000). Parsing also enforces the configured statement file-size limit. Invalid values and wrong-width rows are returned with row diagnostics; empty files, invalid UTF-8, malformed quoting, missing/ambiguous columns, and limit violations return a clear error.
- PDF and XLSX uploads remain stored but cannot be preview-parsed. CSV previews are temporary responses; they are not persisted.

Registration requires email confirmation and supports password reset (see above). Sign-in is rate limited per IP; MFA is not implemented yet. Configure HTTPS, a production data-protection key store, exact frontend origin, database backups, and monitoring before exposing it publicly.

To create optional demo data for your own account, replace the email placeholder in `docs/sample-data.sql` with the email you registered, then run the script against the DollarShark database. It adds a sample account, private category, and three transactions; rerunning it does not duplicate those sample transactions.

## Auto-categorization rules

- Manage rules on the **Categories** page: "when description *contains* / *starts with* / *is exactly* TEXT → set category". Matching ignores upper/lower case and repeated spaces.
- CSV import previews pre-fill each row's category from the first matching rule (marked "⚡ by rule"); you can still change it before confirming. Nothing is saved until the import is confirmed.
- **Common merchants:** one click adds about 90 starter rules for well-known Canadian merchants (Walmart, Superstore, Loblaws, Tim Hortons, Uber Eats, Petro-Canada, Netflix, Rogers, Air Canada…) mapped to the default categories. They are copied into the user's own rules, so each can be edited or deleted; clicking again only re-adds missing ones and never overwrites edits. The list lives in `StarterCategoryRules.cs`.
- **Apply to uncategorized** runs the rules over existing transactions that have no category. Transactions that already have a category are never changed.
- If several rules match, the lowest priority number wins, then the longest text, then the oldest rule. Rules are private per user and can use shared default categories or the user's own categories; deleting a private category deletes its rules. Limit: 200 rules per user.
- The `Phase8CategoryRules` migration adds the `CategoryRules` table; apply it with the usual `database update` command.

New users also see a **getting started** checklist on the Dashboard (add account → upload statement → review & import → optional rule). Steps tick off from real data, and it can be hidden.

## API overview

- Accounts: GET /api/accounts?includeInactive=false, GET /api/accounts/{id}, POST /api/accounts, PUT /api/accounts/{id}, PATCH /api/accounts/{id}/status
- Categories: GET /api/categories, GET /api/categories/{id}, POST /api/categories, PUT /api/categories/{id}, DELETE /api/categories/{id}
- Category rules: GET /api/category-rules, POST /api/category-rules, PUT /api/category-rules/{id}, DELETE /api/category-rules/{id}, POST /api/category-rules/apply
- Transactions: GET /api/transactions, GET /api/transactions/{id}, POST /api/transactions, PUT /api/transactions/{id}, DELETE /api/transactions/{id}
- Statements: POST /api/statements, GET /api/statements, GET /api/statements/{id}, DELETE /api/statements/{id}
- CSV preview: POST /api/statements/{id}/parse-csv
- Dashboard: GET /api/dashboard/overview?from=YYYY-MM-DD&to=YYYY-MM-DD&recentLimit=8

The dashboard endpoint defaults to the full current UTC calendar month. Supply both `from` and `to` for a custom inclusive date range. Full calendar months compare with the previous calendar month; other ranges compare with the immediately preceding range of the same number of days. Totals are returned separately per transaction currency; no currency conversion is performed. Negative non-transfer amounts contribute to positive-valued expense totals, positive non-transfer amounts (including refunds) contribute to income, and explicitly typed transfers are excluded from all cash-flow totals. Recent transactions are limited to the selected period and sorted newest first with a deterministic ID tie-breaker.

Transactions accept year, month, accountId, categoryId, transactionType, search, from, to, sortBy, sortDirection, page, and pageSize query parameters. Results include pagination metadata; page size is limited to 100.

## Build and tests

From the repository root:

~~~powershell
dotnet build backend/DollarShark.sln
dotnet test backend/DollarShark.sln
~~~

From frontend:

~~~powershell
npm run build
~~~

The tests use SQLite in-memory relational databases for integration behavior. They do not apply migrations to or modify your SQL Server database.

## Data conventions and scope

- Expenses are negative; income and refunds are positive. Transfers may use either sign and are typed separately.
- Money uses decimal(19,4) and transaction dates use DateOnly.
- Account deactivation is soft; transaction history stays associated.
- A category in use by transactions or child categories cannot be deleted.
- Account records store only optional last four digits. Never enter full card numbers, CVVs, banking credentials, or account passwords.
- Statement parsing/extraction, automatic categorization, analytics, Telegram, AWS deployment, budgeting, investments, credit card reconciliation, and automatic transfer matching are out of scope.
