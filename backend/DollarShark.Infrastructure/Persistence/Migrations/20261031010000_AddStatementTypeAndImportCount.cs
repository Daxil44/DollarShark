using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DollarShark.Infrastructure.Persistence;

#nullable disable

namespace DollarShark.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20261031010000_AddStatementTypeAndImportCount")]
public partial class AddStatementTypeAndImportCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "StatementType", table: "StatementImports", type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Bank");
        migrationBuilder.AddColumn<int>(name: "ImportedTransactionCount", table: "StatementImports", type: "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StatementType", table: "StatementImports");
        migrationBuilder.DropColumn(name: "ImportedTransactionCount", table: "StatementImports");
    }
}
