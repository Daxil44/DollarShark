# DollarShark

DollarShark is a local, single-user personal finance tracking MVP. Phase 1 establishes the backend, initial data model, and frontend shell. It does not include authentication, statement parsing, analytics, Telegram, or deployment. Do not expose the development API to untrusted networks.

## Prerequisites

- .NET 10 SDK
- Node.js 20.19 or newer and npm (Node 22 LTS is recommended; the installed Node 20.5.1 is below the dependency toolchain minimum)
- SQL Server Developer Edition running locally

## Backend

1. Set a local-only SQL Server connection string using .NET User Secrets (this value is not committed):

   ```powershell
   dotnet user-secrets set "ConnectionStrings:FinanceDb" "Server=localhost;Database=DollarShark;Trusted_Connection=True;TrustServerCertificate=True;" --project backend/DollarShark.Api
   ```

2. From the repository root, run the API:

   ```powershell
   dotnet run --project backend/DollarShark.Api
   ```

   The development profile serves HTTP at `http://localhost:5017`. Check `http://localhost:5017/health`, `http://localhost:5017/api/accounts`, and the OpenAPI document at `http://localhost:5017/openapi/v1.json`. The accounts endpoint queries SQL Server and returns an empty array when there are no accounts, after the initial schema migration has been applied.

3. The initial migration is checked in under Infrastructure. Install the EF CLI locally in this repository, then apply it explicitly when you are ready to create the local schema:

   ```powershell
   dotnet tool install dotnet-ef --version 10.0.13 --tool-path ./.tools
   .\.tools\dotnet-ef.exe database update --project backend/DollarShark.Infrastructure --startup-project backend/DollarShark.Api
   ```

   To scaffold a later migration, run `.\.tools\dotnet-ef.exe migrations add <MigrationName> --project backend/DollarShark.Infrastructure --startup-project backend/DollarShark.Api --output-dir Persistence/Migrations`. No migration or database update is run automatically by the API.

## Frontend

```powershell
cd frontend
npm install
$env:VITE_API_BASE_URL = "http://localhost:5017"
npm run dev
```

Open the Vite URL printed in the terminal (normally `http://localhost:5173`). `VITE_API_BASE_URL` is a public API URL only; never put secrets in `VITE_*` variables.

## Build and tests

From the repository root:

```powershell
dotnet build backend/DollarShark.sln
dotnet test backend/DollarShark.sln
```

From `frontend`:

```powershell
npm run build
```

## Domain conventions

Transaction amounts use a consistent signed convention: credits and income are positive; purchases, withdrawals, and fees are negative. Amount precision is configured as `decimal(19,4)`. Transaction dates are `DateOnly`, independent of time zone. Account records contain only optional last four digits; never store card numbers, CVVs, passwords, or bank credentials. IDs and nullable category assignment leave room for future statement-import and ownership metadata.

## Architecture

- `backend/DollarShark.Domain`: finance entities and domain defaults.
- `backend/DollarShark.Application`: API-facing account DTO and read contract.
- `backend/DollarShark.Infrastructure`: EF Core SQL Server context, mappings, and query implementation; migrations belong here.
- `backend/DollarShark.Api`: composition root, health and accounts endpoints, development-only OpenAPI, and Vite-origin CORS.
- `backend/DollarShark.Tests`: xUnit domain tests.
- `frontend`: React, TypeScript, and Vite dashboard shell.





