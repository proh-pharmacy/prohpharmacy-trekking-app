using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleWarehouseAndStockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrekStockLoads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasicQuantityLoaded = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQuantityLoaded = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    LoadedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekStockLoads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekStockLoads_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekStockLoads_StaffMembers_LoadedByStaffId",
                        column: x => x.LoadedByStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekStockLoads_TrekkingTrips_TrekkingTripId",
                        column: x => x.TrekkingTripId,
                        principalTable: "TrekkingTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleProductStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasicQuantityOnHand = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQuantityOnHand = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleProductStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleProductStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleProductStocks_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehicleStockLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BasicQtyChange = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PackagingQtyChange = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleStockLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleStockLedger_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleStockLedger_StaffMembers_AuthorStaffId",
                        column: x => x.AuthorStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleStockLedger_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrekStockLoads_LoadedByStaffId",
                table: "TrekStockLoads",
                column: "LoadedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekStockLoads_ProductId",
                table: "TrekStockLoads",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekStockLoads_TrekkingTripId_ProductId",
                table: "TrekStockLoads",
                columns: new[] { "TrekkingTripId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrekStockLoads_VehicleId",
                table: "TrekStockLoads",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleProductStocks_ProductId",
                table: "VehicleProductStocks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleProductStocks_VehicleId_ProductId",
                table: "VehicleProductStocks",
                columns: new[] { "VehicleId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStockLedger_AuthorStaffId",
                table: "VehicleStockLedger",
                column: "AuthorStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStockLedger_ProductId",
                table: "VehicleStockLedger",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStockLedger_ReferenceId",
                table: "VehicleStockLedger",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStockLedger_VehicleId_ProductId_RecordedAt",
                table: "VehicleStockLedger",
                columns: new[] { "VehicleId", "ProductId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrekStockLoads");

            migrationBuilder.DropTable(
                name: "VehicleProductStocks");

            migrationBuilder.DropTable(
                name: "VehicleStockLedger");
        }
    }
}
