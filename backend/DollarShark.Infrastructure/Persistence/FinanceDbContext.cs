using DollarShark.Domain;
using DollarShark.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<StatementImport> StatementImports => Set<StatementImport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail)
            .HasDatabaseName("EmailIndex").IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        var account = modelBuilder.Entity<FinancialAccount>();
        account.HasKey(x => x.Id);
        account.Property(x => x.Name).HasMaxLength(120).IsRequired();
        account.Property(x => x.Institution).HasMaxLength(120).IsRequired();
        account.Property(x => x.AccountType).HasMaxLength(40).IsRequired();
        account.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        account.Property(x => x.LastFourDigits).HasMaxLength(4);
        account.HasIndex(x => new { x.Institution, x.Name });
        account.Property(x => x.UserId).HasMaxLength(450);
        account.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        account.HasIndex(x => x.UserId);

        var category = modelBuilder.Entity<Category>();
        category.HasKey(x => x.Id);
        category.Property(x => x.Name).HasMaxLength(80).IsRequired();
        category.Property(x => x.UserId).HasMaxLength(450);
        category.HasIndex(x => new { x.UserId, x.ParentCategoryId, x.Name }).IsUnique()
            .HasFilter("[UserId] IS NOT NULL");
        category.HasIndex(x => x.UserId);
        category.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        category.HasOne(x => x.ParentCategory).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        category.HasData(CategorySeedData.Categories);

        var transaction = modelBuilder.Entity<Transaction>();
        transaction.HasKey(x => x.Id);
        transaction.Property(x => x.Description).HasMaxLength(300).IsRequired();
        transaction.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        transaction.Property(x => x.Amount).HasPrecision(19, 4);
        transaction.Property(x => x.UserId).HasMaxLength(450);
        transaction.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(16)
            .HasDefaultValue(TransactionType.Expense);
        transaction.HasIndex(x => new { x.FinancialAccountId, x.TransactionDate });
        transaction.HasIndex(x => x.CategoryId);
        transaction.HasIndex(x => x.UserId);
        transaction.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        transaction.HasOne(x => x.FinancialAccount).WithMany(x => x.Transactions)
            .HasForeignKey(x => x.FinancialAccountId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasOne(x => x.Category).WithMany(x => x.Transactions)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);

        var statement = modelBuilder.Entity<StatementImport>();
        statement.HasKey(x => x.Id);
        statement.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        statement.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        statement.Property(x => x.StorageKey).HasMaxLength(64).IsRequired();
        statement.Property(x => x.FileSizeBytes).IsRequired();
        statement.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        statement.Property(x => x.FileHash).HasMaxLength(64).IsFixedLength().IsRequired();
        statement.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        statement.HasIndex(x => x.StorageKey).IsUnique();
        statement.HasIndex(x => x.FinancialAccountId);
        statement.HasIndex(x => new { x.UserId, x.UploadedAtUtc });
        statement.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        statement.HasOne(x => x.FinancialAccount).WithMany().HasForeignKey(x => x.FinancialAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public static class CategorySeedData
{
    public static readonly Category[] Categories =
    [
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "Groceries", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "Restaurants", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Transportation", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Housing", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = "Utilities", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Name = "Shopping", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Name = "Subscriptions", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000008"), Name = "Health & Fitness", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000009"), Name = "Travel", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000010"), Name = "Salary", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000011"), Name = "Refunds", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000012"), Name = "Transfers", IsSystem = true },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000013"), Name = "Other", IsSystem = true }
    ];
}
