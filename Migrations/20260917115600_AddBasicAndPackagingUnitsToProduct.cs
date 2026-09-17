using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddBasicAndPackagingUnitsToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unit",
                table: "Products");

            migrationBuilder.AddColumn<Guid>(
                name: "BasicUnitId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Products""
                SET ""BasicUnitId"" = (SELECT ""Id"" FROM ""Units"" LIMIT 1)
                WHERE ""BasicUnitId"" IS NULL;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "BasicUnitId",
                table: "Products",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BasicUnitPrice",
                table: "Products",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PackagingUnitId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingUnitPrice",
                table: "Products",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_BasicUnitId",
                table: "Products",
                column: "BasicUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_PackagingUnitId",
                table: "Products",
                column: "PackagingUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Units_BasicUnitId",
                table: "Products",
                column: "BasicUnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Units_PackagingUnitId",
                table: "Products",
                column: "PackagingUnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Units_BasicUnitId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Units_PackagingUnitId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_BasicUnitId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_PackagingUnitId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BasicUnitId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BasicUnitPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackagingUnitId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackagingUnitPrice",
                table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "Products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }
    }
}
