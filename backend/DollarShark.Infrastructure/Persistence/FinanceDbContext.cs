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
        category.HasOne(x => x.ParentCategory).WithMany(x => x.Children).HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        var transaction = modelBuilder.Entity<Transaction>();
        transaction.HasKey(x => x.Id);
        transaction.Property(x => x.Description).HasMaxLength(300).IsRequired();
        transaction.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        transaction.Property(x => x.Amount).HasPrecision(19, 4);
        transaction.HasIndex(x => new { x.FinancialAccountId, x.TransactionDate });
        transaction.HasIndex(x => x.CategoryId);
        transaction.HasOne(x => x.FinancialAccount).WithMany(x => x.Transactions).HasForeignKey(x => x.FinancialAccountId).OnDelete(DeleteBehavior.Cascade);
        transaction.HasOne(x => x.Category).WithMany(x => x.Transactions).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
