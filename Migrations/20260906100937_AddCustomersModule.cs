using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TradingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryPhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WhatsAppNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    OwningBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RegisteredByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredDuringTrekId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAccounts_Branches_OwningBranchId",
                        column: x => x.OwningBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAccounts_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerAccounts_StaffMembers_RegisteredByStaffId",
                        column: x => x.RegisteredByStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreetAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LandmarkAndDirections = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    AccuracyMetres = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    CaptureMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CapturedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerLocations_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerLocations_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLocations_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerLocations_StaffMembers_CapturedByStaffId",
                        column: x => x.CapturedByStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPersons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MiddleName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RelationshipType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PrimaryPhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AlternativePhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    EmailAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GhanaCardNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsPrimaryContact = table.Column<bool>(type: "boolean", nullable: false),
                    IsCreditResponsiblePerson = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPersons_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_BusinessName",
                table: "CustomerAccounts",
                column: "BusinessName");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_CustomerCode",
                table: "CustomerAccounts",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_OwningBranchId",
                table: "CustomerAccounts",
                column: "OwningBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_PrimaryPhoneNumber",
                table: "CustomerAccounts",
                column: "PrimaryPhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_RegionId",
                table: "CustomerAccounts",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_RegisteredByStaffId",
                table: "CustomerAccounts",
                column: "RegisteredByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLocations_CapturedByStaffId",
                table: "CustomerLocations",
                column: "CapturedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLocations_CustomerAccountId",
                table: "CustomerLocations",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLocations_DistrictId",
                table: "CustomerLocations",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLocations_RegionId",
                table: "CustomerLocations",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPersons_CustomerAccountId",
                table: "CustomerPersons",
                column: "CustomerAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerLocations");

            migrationBuilder.DropTable(
                name: "CustomerPersons");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");
        }
    }
}
