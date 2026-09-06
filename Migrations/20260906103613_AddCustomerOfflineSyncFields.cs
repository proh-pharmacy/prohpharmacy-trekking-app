using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOfflineSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientGeneratedId",
                table: "CustomerAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreatedOffline",
                table: "CustomerAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordedAt",
                table: "CustomerAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_ClientGeneratedId",
                table: "CustomerAccounts",
                column: "ClientGeneratedId",
                unique: true,
                filter: "\"ClientGeneratedId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerAccounts_ClientGeneratedId",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "ClientGeneratedId",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "CreatedOffline",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "CustomerAccounts");
        }
    }
}
