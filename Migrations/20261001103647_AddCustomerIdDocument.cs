using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerIdDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdCardBackUrl",
                table: "CustomerAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdCardFrontUrl",
                table: "CustomerAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdDocumentNumber",
                table: "CustomerAccounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdDocumentType",
                table: "CustomerAccounts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_IdDocumentNumber",
                table: "CustomerAccounts",
                column: "IdDocumentNumber",
                unique: true,
                filter: "\"IdDocumentNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerAccounts_IdDocumentNumber",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "IdCardBackUrl",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "IdCardFrontUrl",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "IdDocumentNumber",
                table: "CustomerAccounts");

            migrationBuilder.DropColumn(
                name: "IdDocumentType",
                table: "CustomerAccounts");
        }
    }
}
