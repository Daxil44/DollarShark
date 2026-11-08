namespace DollarShark.Infrastructure.Persistence;

/// <summary>
/// Integration tests share one in-memory SQLite connection across requests, and SqliteConnection is not safe for
/// concurrent use. Read paths that can run in parallel during the same request (duplicate checks, rule matching)
/// take this gate when running on SQLite. SQL Server is unaffected.
/// </summary>
internal static class SqliteQueryGate
{
    public static readonly SemaphoreSlim Instance = new(1, 1);

    public static bool IsSqlite(FinanceDbContext db) => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
}
