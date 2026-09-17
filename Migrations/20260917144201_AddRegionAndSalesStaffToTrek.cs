using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionAndSalesStaffToTrek : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "BranchId",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""TrekkingTrips""
                SET ""RegionId"" = (SELECT ""Id"" FROM ""Regions"" LIMIT 1)
                WHERE ""RegionId"" IS NULL;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "RegionId",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesStaffId",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_RegionId",
                table: "TrekkingTrips",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTrips_SalesStaffId",
                table: "TrekkingTrips",
                column: "SalesStaffId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrekkingTrips_Regions_RegionId",
                table: "TrekkingTrips",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrekkingTrips_StaffMembers_SalesStaffId",
                table: "TrekkingTrips",
                column: "SalesStaffId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrekkingTrips_Regions_RegionId",
                table: "TrekkingTrips");

            migrationBuilder.DropForeignKey(
                name: "FK_TrekkingTrips_StaffMembers_SalesStaffId",
                table: "TrekkingTrips");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTrips_RegionId",
                table: "TrekkingTrips");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTrips_SalesStaffId",
                table: "TrekkingTrips");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "TrekkingTrips");

            migrationBuilder.DropColumn(
                name: "SalesStaffId",
                table: "TrekkingTrips");

            migrationBuilder.AlterColumn<Guid>(
                name: "BranchId",
                table: "TrekkingTrips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
