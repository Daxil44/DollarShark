using DollarShark.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DollarShark.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20261025233000_AddTransactionExternalReference")]
public sealed class AddTransactionExternalReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExternalReference",
            table: "Transactions",
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "DuplicateWarningOverridden",
            table: "Transactions",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_FinancialAccountId_ExternalReference",
            table: "Transactions",
            columns: new[] { "FinancialAccountId", "ExternalReference" },
            filter: "[ExternalReference] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Transactions_FinancialAccountId_ExternalReference",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "ExternalReference",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "DuplicateWarningOverridden",
            table: "Transactions");
    }
}
