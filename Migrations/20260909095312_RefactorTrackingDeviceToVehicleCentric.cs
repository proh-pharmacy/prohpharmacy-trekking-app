using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class RefactorTrackingDeviceToVehicleCentric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffDeviceAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "TrackingDevices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "TrackingDevices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDevices_StaffMemberId",
                table: "TrackingDevices",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDevices_VehicleId",
                table: "TrackingDevices",
                column: "VehicleId",
                unique: true,
                filter: "\"VehicleId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_TrackingDevices_StaffMembers_StaffMemberId",
                table: "TrackingDevices",
                column: "StaffMemberId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackingDevices_Vehicles_VehicleId",
                table: "TrackingDevices",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrackingDevices_StaffMembers_StaffMemberId",
                table: "TrackingDevices");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackingDevices_Vehicles_VehicleId",
                table: "TrackingDevices");

            migrationBuilder.DropIndex(
                name: "IX_TrackingDevices_StaffMemberId",
                table: "TrackingDevices");

            migrationBuilder.DropIndex(
                name: "IX_TrackingDevices_VehicleId",
                table: "TrackingDevices");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "TrackingDevices");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "TrackingDevices");

            migrationBuilder.CreateTable(
                name: "StaffDeviceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnassignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDeviceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffDeviceAssignments_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffDeviceAssignments_TrackingDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "TrackingDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeviceAssignments_DeviceId_UnassignedAt",
                table: "StaffDeviceAssignments",
                columns: new[] { "DeviceId", "UnassignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeviceAssignments_StaffMemberId_UnassignedAt",
                table: "StaffDeviceAssignments",
                columns: new[] { "StaffMemberId", "UnassignedAt" });
        }
    }
}
