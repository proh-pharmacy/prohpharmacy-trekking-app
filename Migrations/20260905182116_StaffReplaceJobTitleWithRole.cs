using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class StaffReplaceJobTitleWithRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffMembers_EmployeeNumber",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "StaffMembers");

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeNumber",
                table: "StaffMembers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "StaffMembers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_EmployeeNumber",
                table: "StaffMembers",
                column: "EmployeeNumber",
                unique: true,
                filter: "\"EmployeeNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffMembers_EmployeeNumber",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "StaffMembers");

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeNumber",
                table: "StaffMembers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "StaffMembers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_EmployeeNumber",
                table: "StaffMembers",
                column: "EmployeeNumber",
                unique: true);
        }
    }
}
