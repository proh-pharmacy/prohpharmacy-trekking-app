using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionToVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "Vehicles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_RegionId",
                table: "Vehicles",
                column: "RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Regions_RegionId",
                table: "Vehicles",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Regions_RegionId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_RegionId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "Vehicles");
        }
    }
}
