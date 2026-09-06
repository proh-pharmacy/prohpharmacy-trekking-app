using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsAndTrekkingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrekkingTrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DriverStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekkingTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekkingTrips_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTrips_StaffMembers_DriverStaffId",
                        column: x => x.DriverStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTrips_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrekkingTripStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekkingTripStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStops_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStops_TrekkingTrips_TrekkingTripId",
                        column: x => x.TrekkingTripId,
                        principalTable: "TrekkingTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrekkingTripStopProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrekkingTripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrekkingTripStopProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStopProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrekkingTripStopProducts_TrekkingTripStops_TrekkingTripStop~",
                        column: x => x.TrekkingTripStopId,
                        principalTable: "TrekkingTripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_BranchId",
                table: "TrekkingTrips",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_DriverStaffId",
                table: "TrekkingTrips",
                column: "DriverStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_TrekNumber",
                table: "TrekkingTrips",
                column: "TrekNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_VehicleId",
                table: "TrekkingTrips",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopProducts_ProductId",
                table: "TrekkingTripStopProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopProducts_TrekkingTripStopId",
                table: "TrekkingTripStopProducts",
                column: "TrekkingTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStops_CustomerAccountId",
                table: "TrekkingTripStops",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStops_TrekkingTripId_Sequence",
                table: "TrekkingTripStops",
                columns: new[] { "TrekkingTripId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrekkingTripStopProducts");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "TrekkingTripStops");

            migrationBuilder.DropTable(
                name: "TrekkingTrips");
        }
    }
}
