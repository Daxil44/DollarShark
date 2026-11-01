using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DollarShark.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase65LinkedTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedTransferTransactionId",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_LinkedTransferTransactionId",
                table: "Transactions",
                column: "LinkedTransferTransactionId",
                unique: true,
                filter: "[LinkedTransferTransactionId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_LinkedTransferTransactionId",
                table: "Transactions",
                column: "LinkedTransferTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_LinkedTransferTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_LinkedTransferTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LinkedTransferTransactionId",
                table: "Transactions");
        }
    }
}
