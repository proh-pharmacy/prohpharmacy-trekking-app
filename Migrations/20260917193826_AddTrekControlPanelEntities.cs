using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddTrekControlPanelEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWalkIn",
                table: "TrekkingTripStops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnplanned",
                table: "TrekkingTripStopProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TrekkingTripStopReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasicQtyReturned = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQtyReturned = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    BasicUnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    PackagingUnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    RefundAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    RefundMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientGeneratedId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    GpsAccuracyMetres = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekkingTripStopReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStopReturns_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStopReturns_StaffMembers_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStopReturns_TrekkingTripStops_TrekkingTripStopId",
                        column: x => x.TrekkingTripStopId,
                        principalTable: "TrekkingTripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_ClientGeneratedId",
                table: "TrekkingTripStopReturns",
                column: "ClientGeneratedId",
                unique: true,
                filter: "\"ClientGeneratedId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_ProductId",
                table: "TrekkingTripStopReturns",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_RecordedByStaffId",
                table: "TrekkingTripStopReturns",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_TrekkingTripStopId",
                table: "TrekkingTripStopReturns",
                column: "TrekkingTripStopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "IsWalkIn",
                table: "TrekkingTripStops");

            migrationBuilder.DropColumn(
                name: "IsUnplanned",
                table: "TrekkingTripStopProducts");
        }
    }
}
