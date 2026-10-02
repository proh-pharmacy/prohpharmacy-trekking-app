using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class SplitVehicleStockLedgerBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BalanceAfter",
                table: "VehicleStockLedger",
                newName: "BasicBalanceAfter");

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingBalanceAfter",
                table: "VehicleStockLedger",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackagingBalanceAfter",
                table: "VehicleStockLedger");

            migrationBuilder.RenameColumn(
                name: "BasicBalanceAfter",
                table: "VehicleStockLedger",
                newName: "BalanceAfter");
        }
    }
}
