using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddClientGeneratedIdToStopAndProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientGeneratedId",
                table: "TrekkingTripStops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientGeneratedId",
                table: "TrekkingTripStopProducts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStops_ClientGeneratedId",
                table: "TrekkingTripStops",
                column: "ClientGeneratedId",
                unique: true,
                filter: "\"ClientGeneratedId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopProducts_ClientGeneratedId",
                table: "TrekkingTripStopProducts",
                column: "ClientGeneratedId",
                unique: true,
                filter: "\"ClientGeneratedId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrekkingTripStops_ClientGeneratedId",
                table: "TrekkingTripStops");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTripStopProducts_ClientGeneratedId",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "ClientGeneratedId",
                table: "TrekkingTripStops");

            migrationBuilder.DropColumn(
                name: "ClientGeneratedId",
                table: "TrekkingTripStopProducts");
        }
    }
}
