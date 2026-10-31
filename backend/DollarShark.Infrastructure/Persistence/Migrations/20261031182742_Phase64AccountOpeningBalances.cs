using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DollarShark.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase64AccountOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "FinancialAccounts",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpeningBalanceDate",
                table: "FinancialAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialAccounts_OpeningBalance",
                table: "FinancialAccounts",
                sql: "([OpeningBalance] IS NULL AND [OpeningBalanceDate] IS NULL) OR ([OpeningBalance] IS NOT NULL AND [OpeningBalanceDate] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialAccounts_OpeningBalance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceDate",
                table: "FinancialAccounts");
        }
    }
}
