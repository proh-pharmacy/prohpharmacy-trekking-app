using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotProductPricesOnStopProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BasicUnitPrice",
                table: "TrekkingTripStopProducts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingUnitPrice",
                table: "TrekkingTripStopProducts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasicUnitPrice",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "PackagingUnitPrice",
                table: "TrekkingTripStopProducts");
        }
    }
}
