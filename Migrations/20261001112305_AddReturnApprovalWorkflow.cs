using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "TrekkingTripStopReturns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "TrekkingTripStopReturns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByStaffId",
                table: "TrekkingTripStopReturns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "TrekkingTripStopReturns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleInvoiceId",
                table: "TrekkingTripStopReturns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_ApprovalStatus",
                table: "TrekkingTripStopReturns",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_ApprovedByStaffId",
                table: "TrekkingTripStopReturns",
                column: "ApprovedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_TrekkingTripStopReturns_SaleInvoiceId",
                table: "TrekkingTripStopReturns",
                column: "SaleInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrekkingTripStopReturns_SaleInvoices_SaleInvoiceId",
                table: "TrekkingTripStopReturns",
                column: "SaleInvoiceId",
                principalTable: "SaleInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrekkingTripStopReturns_StaffMembers_ApprovedByStaffId",
                table: "TrekkingTripStopReturns",
                column: "ApprovedByStaffId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrekkingTripStopReturns_SaleInvoices_SaleInvoiceId",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_TrekkingTripStopReturns_StaffMembers_ApprovedByStaffId",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTripStopReturns_ApprovalStatus",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTripStopReturns_ApprovedByStaffId",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropIndex(
                name: "IX_TrekkingTripStopReturns_SaleInvoiceId",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "ApprovedByStaffId",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "TrekkingTripStopReturns");

            migrationBuilder.DropColumn(
                name: "SaleInvoiceId",
                table: "TrekkingTripStopReturns");
        }
    }
}
