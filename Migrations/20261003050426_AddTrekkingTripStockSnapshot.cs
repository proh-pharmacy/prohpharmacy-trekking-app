using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddTrekkingTripStockSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrekkingTripStockSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BasicUnitName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PackagingUnitName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BasicUnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    PackagingUnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    BasicQtyAtStart = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQtyAtStart = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    BasicQtySold = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQtySold = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    BasicQtyRemaining = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQtyRemaining = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    RevenueAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekkingTripStockSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStockSnapshots_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStockSnapshots_TrekkingTrips_TrekkingTripId",
                        column: x => x.TrekkingTripId,
                        principalTable: "TrekkingTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStockSnapshots_ProductId",
                table: "TrekkingTripStockSnapshots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStockSnapshots_TrekkingTripId_ProductId",
                table: "TrekkingTripStockSnapshots",
                columns: new[] { "TrekkingTripId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrekkingTripStockSnapshots");
        }
    }
}
