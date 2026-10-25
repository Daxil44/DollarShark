# DollarShark architecture

DollarShark is a single-user ASP.NET Core API with a React client. The API is the composition root. Domain owns finance entities and transaction type rules; Application owns use-case contracts, validation, and transport DTOs; Infrastructure owns EF Core and SQL Server persistence; API controllers handle HTTP concerns. The frontend uses shared Axios configuration and React Bootstrap pages.

Financial accounts are deactivated through IsActive; transaction history stays associated. Category deletion is refused while transactions or children reference the category. Categories are seeded by migration with fixed IDs so database updates are repeatable.

Expenses are negative, income and refunds are positive, and transfers may carry either sign while remaining a separate transaction type. Money retains decimal(19,4) precision and dates use DateOnly. Each transaction uses the currency of its financial account. No exchange-rate conversion is implied.

The Phase 2 migration adds a string-backed TransactionType column with Expense as the default for existing rows and inserts default categories. Apply it with EF tooling; the API never applies migrations automatically. Keep the API on localhost. OpenAPI is available only in Development, and development CORS permits the Vite origin only. Authentication and multi-user security are not implemented.
