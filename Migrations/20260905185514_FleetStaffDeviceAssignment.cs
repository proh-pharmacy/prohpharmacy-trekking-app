using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class FleetStaffDeviceAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleDeviceAssignments");

            migrationBuilder.CreateTable(
                name: "StaffDeviceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnassignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffDeviceAssignments");

            migrationBuilder.CreateTable(
                name: "VehicleDeviceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnassignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleDeviceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleDeviceAssignments_TrackingDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "TrackingDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleDeviceAssignments_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleDeviceAssignments_DeviceId_UnassignedAt",
                table: "VehicleDeviceAssignments",
                columns: new[] { "DeviceId", "UnassignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleDeviceAssignments_VehicleId",
                table: "VehicleDeviceAssignments",
                column: "VehicleId");
        }
    }
}
