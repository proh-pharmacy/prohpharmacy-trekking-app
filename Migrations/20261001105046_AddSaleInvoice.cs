using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceNumberTrackers",
                columns: table => new
                {
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastSequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberTrackers", x => new { x.ScopeId, x.ScopeType });
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TrekkingTripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    TotalPaid = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ClientGeneratedId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOffline = table.Column<bool>(type: "boolean", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_TrekkingTripStops_TrekkingTripStopId",
                        column: x => x.TrekkingTripStopId,
                        principalTable: "TrekkingTripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_ClientGeneratedId",
                table: "SaleInvoices",
                column: "ClientGeneratedId",
                unique: true,
                filter: "\"ClientGeneratedId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_CustomerAccountId",
                table: "SaleInvoices",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_InvoiceNumber",
                table: "SaleInvoices",
                column: "InvoiceNumber",
                unique: true,
                filter: "\"InvoiceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_IssuedAt",
                table: "SaleInvoices",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_TrekkingTripId",
                table: "SaleInvoices",
                column: "TrekkingTripId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_TrekkingTripStopId",
                table: "SaleInvoices",
                column: "TrekkingTripStopId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceNumberTrackers");

            migrationBuilder.DropTable(
                name: "SaleInvoices");
        }
    }
}
