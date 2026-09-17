using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagingQuantitiesToStopProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlannedQuantity",
                table: "TrekkingTripStopProducts",
                newName: "PlannedBasicQuantity");

            migrationBuilder.RenameColumn(
                name: "QtyDelivered",
                table: "TrekkingTripStopProducts",
                newName: "BasicQtyDelivered");

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedPackagingQuantity",
                table: "TrekkingTripStopProducts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingQtyDelivered",
                table: "TrekkingTripStopProducts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannedPackagingQuantity",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "PackagingQtyDelivered",
                table: "TrekkingTripStopProducts");

            migrationBuilder.RenameColumn(
                name: "BasicQtyDelivered",
                table: "TrekkingTripStopProducts",
                newName: "QtyDelivered");

            migrationBuilder.RenameColumn(
                name: "PlannedBasicQuantity",
                table: "TrekkingTripStopProducts",
                newName: "PlannedQuantity");
        }
    }
}
