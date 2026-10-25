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
- Registration signs in the new user. Identity stores a salted password hash; the API never returns the password or hash.
- Authentication uses an HttpOnly cookie (8-hour lifetime, sliding expiration, SameSite=Lax). HTTPS requests receive Secure cookies; local HTTP development does not.
- Before any POST, PUT, PATCH, or DELETE, the frontend obtains an antiforgery request token from `/api/auth/csrf` and sends it in `X-CSRF-TOKEN`. The request token stays in memory, never localStorage/sessionStorage.
- Development CORS permits only `http://localhost:5173` by default (override with `ViteOrigin`). Production must set `FrontendOrigin` to the exact HTTPS frontend origin.
- The React dev URL and API use different ports on `localhost`, which remain same-site for SameSite=Lax cookies. For production, use one site or same-site subdomains behind HTTPS. A separate cross-site frontend needs a deliberate `SameSite=None; Secure` deployment configuration and corresponding CSRF/CORS review.

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

Email confirmation and password reset are deferred. Registration is open and currently has no email verification, recovery, MFA, or dedicated per-IP rate limit; do not treat this phase as ready for unrestricted public registration. Configure HTTPS, a production data-protection key store, exact frontend origin, database backups, and monitoring before exposing it publicly.

To create optional demo data for your own account, replace the email placeholder in `docs/sample-data.sql` with the email you registered, then run the script against the DollarShark database. It adds a sample account, private category, and three transactions; rerunning it does not duplicate those sample transactions.

## API overview

- Accounts: GET /api/accounts?includeInactive=false, GET /api/accounts/{id}, POST /api/accounts, PUT /api/accounts/{id}, PATCH /api/accounts/{id}/status
- Categories: GET /api/categories, GET /api/categories/{id}, POST /api/categories, PUT /api/categories/{id}, DELETE /api/categories/{id}
- Transactions: GET /api/transactions, GET /api/transactions/{id}, POST /api/transactions, PUT /api/transactions/{id}, DELETE /api/transactions/{id}
- Statements: POST /api/statements, GET /api/statements, GET /api/statements/{id}, DELETE /api/statements/{id}
- CSV preview: POST /api/statements/{id}/parse-csv

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
