using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddTrekDriverTokenAndDeliveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmtPaid",
                table: "TrekkingTripStopProducts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "TrekkingTripStopProducts",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "TrekkingTripStopProducts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "TrekkingTripStopProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "TrekkingTripStopProducts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QtyDelivered",
                table: "TrekkingTripStopProducts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverToken",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_DriverToken",
                table: "TrekkingTrips",
                column: "DriverToken",
                unique: true,
                filter: "\"DriverToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrekkingTrips_DriverToken",
                table: "TrekkingTrips");

            migrationBuilder.DropColumn(
                name: "AmtPaid",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "QtyDelivered",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "DriverToken",
                table: "TrekkingTrips");
        }
    }
}
