using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DollarShark.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionSubDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubDescription",
                table: "Transactions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubDescription",
                table: "Transactions");
        }
    }
}
