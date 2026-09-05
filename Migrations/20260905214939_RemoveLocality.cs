using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Localities_LocalityId",
                table: "Branches");

            migrationBuilder.DropTable(
                name: "Localities");

            migrationBuilder.DropIndex(
                name: "IX_Branches_LocalityId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "LocalityId",
                table: "Branches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocalityId",
                table: "Branches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Localities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Localities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Localities_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_LocalityId",
                table: "Branches",
                column: "LocalityId");

            migrationBuilder.CreateIndex(
                name: "IX_Localities_DistrictId_Code",
                table: "Localities",
                columns: new[] { "DistrictId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Localities_DistrictId_Name",
                table: "Localities",
                columns: new[] { "DistrictId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_Localities_LocalityId",
                table: "Branches",
                column: "LocalityId",
                principalTable: "Localities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
