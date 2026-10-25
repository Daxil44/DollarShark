using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<FinancialAccount>();
        account.HasKey(x => x.Id);
        account.Property(x => x.Name).HasMaxLength(120).IsRequired();
        account.Property(x => x.Institution).HasMaxLength(120).IsRequired();
        account.Property(x => x.AccountType).HasMaxLength(40).IsRequired();
        account.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        account.Property(x => x.LastFourDigits).HasMaxLength(4);
        account.HasIndex(x => new { x.Institution, x.Name });

        var category = modelBuilder.Entity<Category>();
        category.HasKey(x => x.Id);
        category.Property(x => x.Name).HasMaxLength(80).IsRequired();
        category.HasIndex(x => new { x.ParentCategoryId, x.Name }).IsUnique();
        category.HasOne(x => x.ParentCategory).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        category.HasData(CategorySeedData.Categories);

        var transaction = modelBuilder.Entity<Transaction>();
        transaction.HasKey(x => x.Id);
        transaction.Property(x => x.Description).HasMaxLength(300).IsRequired();
        transaction.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        transaction.Property(x => x.Amount).HasPrecision(19, 4);
        transaction.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(16)
            .HasDefaultValue(TransactionType.Expense);
        transaction.HasIndex(x => new { x.FinancialAccountId, x.TransactionDate });
        transaction.HasIndex(x => x.CategoryId);
        transaction.HasOne(x => x.FinancialAccount).WithMany(x => x.Transactions)
            .HasForeignKey(x => x.FinancialAccountId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasOne(x => x.Category).WithMany(x => x.Transactions)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}

public static class CategorySeedData
{
    public static readonly Category[] Categories =
    [
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "Groceries" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "Restaurants" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Transportation" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Housing" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Name = "Utilities" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Name = "Shopping" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Name = "Subscriptions" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000008"), Name = "Health & Fitness" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000009"), Name = "Travel" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000010"), Name = "Salary" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000011"), Name = "Refunds" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000012"), Name = "Transfers" },
        new() { Id = Guid.Parse("10000000-0000-0000-0000-000000000013"), Name = "Other" }
    ];
}
