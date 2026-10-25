# Phase 1 architecture

DollarShark is a single ASP.NET Core API with a React client. The API is the composition root. Domain owns finance entities; Application owns use-case contracts and transport DTOs; Infrastructure owns EF Core and SQL Server configuration. API handlers call application contracts and return DTOs. No layer depends on a separate service or deployment platform.

The initial transaction ledger uses positive values for credits and income and negative values for spending, withdrawals, and fees. The transaction currency is stored alongside each amount; no exchange-rate conversion is implied. Statement import IDs, ownership, and transfer matching can be added in later phases without storing banking credentials.

The local API has no authentication or authorization. Keep it on localhost. OpenAPI is exposed only in Development and CORS accepts the Vite development origin only.
