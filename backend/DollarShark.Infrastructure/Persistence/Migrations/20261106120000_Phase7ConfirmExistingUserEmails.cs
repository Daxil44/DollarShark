using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DollarShark.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Email confirmation becomes required for sign-in. Accounts created before that requirement existed are
    /// treated as confirmed so their owners are not locked out. No schema change; new sign-ups are unaffected.
    /// </summary>
    [DbContext(typeof(FinanceDbContext))]
    [Migration("20261106120000_Phase7ConfirmExistingUserEmails")]
    public partial class Phase7ConfirmExistingUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [EmailConfirmed] = 1 WHERE [EmailConfirmed] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: confirmation state cannot be distinguished after the fact.
        }
    }
}
