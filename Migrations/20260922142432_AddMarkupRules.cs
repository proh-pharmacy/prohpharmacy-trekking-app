using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddMarkupRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerMarkupRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkupPercentage = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerMarkupRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerMarkupRules_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerMarkupRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegionalMarkupRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkupPercentage = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionalMarkupRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegionalMarkupRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegionalMarkupRules_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarkupRules_CustomerAccountId",
                table: "CustomerMarkupRules",
                column: "CustomerAccountId",
                unique: true,
                filter: "\"ProductId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarkupRules_CustomerAccountId_ProductId",
                table: "CustomerMarkupRules",
                columns: new[] { "CustomerAccountId", "ProductId" },
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMarkupRules_ProductId",
                table: "CustomerMarkupRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RegionalMarkupRules_ProductId",
                table: "RegionalMarkupRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RegionalMarkupRules_RegionId",
                table: "RegionalMarkupRules",
                column: "RegionId",
                unique: true,
                filter: "\"ProductId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegionalMarkupRules_RegionId_ProductId",
                table: "RegionalMarkupRules",
                columns: new[] { "RegionId", "ProductId" },
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerMarkupRules");

            migrationBuilder.DropTable(
                name: "RegionalMarkupRules");
        }
    }
}
