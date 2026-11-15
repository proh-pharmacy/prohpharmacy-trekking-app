using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleStockBasicUnitPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BasicUnitPrice",
                table: "VehicleProductStocks",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesVehicleStockPrice",
                table: "TrekkingTripStopProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_VehicleProductStocks_BasicUnitPrice",
                table: "VehicleProductStocks",
                sql: "\"BasicUnitPrice\" IS NULL OR \"BasicUnitPrice\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VehicleProductStocks_BasicUnitPrice",
                table: "VehicleProductStocks");

            migrationBuilder.DropColumn(
                name: "BasicUnitPrice",
                table: "VehicleProductStocks");

            migrationBuilder.DropColumn(
                name: "UsesVehicleStockPrice",
                table: "TrekkingTripStopProducts");
        }
    }
}
