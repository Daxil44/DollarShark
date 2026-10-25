# DollarShark

DollarShark is a local, single-user personal finance MVP. Phase 2 provides account, category, and transaction management. There is no authentication or multi-user security; keep the API bound to localhost and do not expose it to untrusted networks.

## Prerequisites

- .NET 10 SDK
- Node.js 20.19 or newer and npm (Node 22 LTS or newer recommended)
- SQL Server Developer Edition running locally

## Database setup

Set the SQL Server connection string in .NET User Secrets. Replace YOUR_SERVER\YOUR_INSTANCE with the server/instance shown by your SQL Server connection:

~~~powershell
dotnet user-secrets set "ConnectionStrings:FinanceDb" "Server=YOUR_SERVER\YOUR_INSTANCE;Database=DollarShark;Trusted_Connection=True;TrustServerCertificate=True;" --project backend/DollarShark.Api
~~~

Apply the checked-in migrations explicitly. The Phase 2 migration adds TransactionType with a default of Expense for existing transaction rows and seeds the default categories. It preserves existing tables and data.

~~~powershell
.\.tools\dotnet-ef.exe database update --project backend/DollarShark.Infrastructure --startup-project backend/DollarShark.Api
~~~

The API does not update the database automatically.

## Run from Visual Studio

Open backend/DollarShark.sln, select the DollarShark Full Stack launch profile, and press F5. The API listens on http://localhost:5017. Its configured SPA proxy starts Vite on http://localhost:5173. Open that Vite URL in your browser. Run npm install in frontend once if dependencies are not installed yet.

## Run from a terminal

From the repository root, run the API using its Development launch profile. The SPA proxy starts the Vite development server, so do not start another npm run dev process at the same time.

~~~powershell
dotnet run --project backend/DollarShark.Api
~~~

Open http://localhost:5173. The checked-in development environment file points the frontend at http://localhost:5017.

## API overview

- Accounts: GET /api/accounts?includeInactive=false, GET /api/accounts/{id}, POST /api/accounts, PUT /api/accounts/{id}, PATCH /api/accounts/{id}/status
- Categories: GET /api/categories, GET /api/categories/{id}, POST /api/categories, PUT /api/categories/{id}, DELETE /api/categories/{id}
- Transactions: GET /api/transactions, GET /api/transactions/{id}, POST /api/transactions, PUT /api/transactions/{id}, DELETE /api/transactions/{id}

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
- Statement uploads, automatic categorization, analytics, Telegram, deployment, authentication, budgeting, investments, credit card reconciliation, and automatic transfer matching are out of scope.
