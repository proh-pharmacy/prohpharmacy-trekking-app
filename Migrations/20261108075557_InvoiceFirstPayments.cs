using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceFirstPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "TrekkingTripStopProducts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "SaleInvoices",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoicePaymentId",
                table: "CustomerLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleInvoiceId",
                table: "CustomerLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientGeneratedId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvoiceTotalAtPayment = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    BalanceAfterPayment = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    IsLegacy = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayments", x => x.Id);
                    table.CheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_InvoicePayments_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoicePayments_StaffMembers_RecordedByStaffId",
                        column: x => x.RecordedByStaffId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_InvoicePaymentId",
                table: "CustomerLedgerEntries",
                column: "InvoicePaymentId",
                unique: true,
                filter: "\"InvoicePaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_SaleInvoiceId",
                table: "CustomerLedgerEntries",
                column: "SaleInvoiceId",
                unique: true,
                filter: "\"SaleInvoiceId\" IS NOT NULL AND \"InvoicePaymentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_ClientGeneratedId",
                table: "InvoicePayments",
                column: "ClientGeneratedId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_ReceiptNumber",
                table: "InvoicePayments",
                column: "ReceiptNumber",
                unique: true,
                filter: "\"ReceiptNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_RecordedByStaffId",
                table: "InvoicePayments",
                column: "RecordedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_SaleInvoiceId",
                table: "InvoicePayments",
                column: "SaleInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerLedgerEntries_InvoicePayments_InvoicePaymentId",
                table: "CustomerLedgerEntries",
                column: "InvoicePaymentId",
                principalTable: "InvoicePayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerLedgerEntries_SaleInvoices_SaleInvoiceId",
                table: "CustomerLedgerEntries",
                column: "SaleInvoiceId",
                principalTable: "SaleInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Give existing stops an invoice without rewriting existing invoice identities or totals.
            migrationBuilder.Sql("""
                DO $$
                DECLARE rec record; seq bigint; invoice_id uuid; billed numeric; paid numeric; invoice_status text;
                BEGIN
                    FOR rec IN
                        SELECT s."Id" AS stop_id, s."TrekkingTripId" AS trip_id, s."CustomerAccountId" AS customer_id,
                               t."Status" AS trek_status, t."CreatedAt" AS created_at,
                               COALESCE(t."BranchId", t."RegionId") AS scope_id,
                               CASE WHEN t."BranchId" IS NULL THEN 'Region' ELSE 'Branch' END AS scope_type,
                               COALESCE(b."Code", r."Code", 'GH') AS scope_code
                        FROM "TrekkingTripStops" s
                        JOIN "TrekkingTrips" t ON t."Id" = s."TrekkingTripId"
                        JOIN "Regions" r ON r."Id" = t."RegionId"
                        LEFT JOIN "Branches" b ON b."Id" = t."BranchId"
                        WHERE NOT EXISTS (SELECT 1 FROM "SaleInvoices" i WHERE i."TrekkingTripStopId" = s."Id")
                        ORDER BY t."CreatedAt", s."Id"
                    LOOP
                        INSERT INTO "InvoiceNumberTrackers" ("ScopeId", "ScopeType", "LastSequence")
                        VALUES (rec.scope_id, rec.scope_type, 1)
                        ON CONFLICT ("ScopeId", "ScopeType") DO UPDATE
                        SET "LastSequence" = "InvoiceNumberTrackers"."LastSequence" + 1
                        RETURNING "LastSequence" INTO seq;
                        SELECT COALESCE(SUM(ROUND(
                            CASE WHEN p."DeliveredAt" IS NOT NULL OR p."BasicQtyDelivered" IS NOT NULL OR p."PackagingQtyDelivered" IS NOT NULL
                                      OR rec.trek_status = 'Completed'
                            THEN COALESCE(p."BasicQtyDelivered", 0) * p."BasicUnitPrice"
                               + COALESCE(p."PackagingQtyDelivered", 0) * COALESCE(p."PackagingUnitPrice", 0)
                            ELSE p."PlannedBasicQuantity" * p."BasicUnitPrice"
                               + COALESCE(p."PlannedPackagingQuantity", 0) * COALESCE(p."PackagingUnitPrice", 0) END, 2)), 0),
                               COALESCE(SUM(p."AmtPaid"), 0)
                          INTO billed, paid FROM "TrekkingTripStopProducts" p WHERE p."TrekkingTripStopId" = rec.stop_id;
                        invoice_status := CASE WHEN rec.trek_status = 'Cancelled' THEN 'Voided'
                            WHEN billed > 0 AND paid >= billed THEN 'Paid'
                            WHEN paid > 0 THEN 'PartiallyPaid' ELSE 'Issued' END;
                        invoice_id := gen_random_uuid();
                        INSERT INTO "SaleInvoices" ("Id", "InvoiceNumber", "TrekkingTripStopId", "TrekkingTripId", "CustomerAccountId",
                            "TotalAmount", "TotalPaid", "Balance", "Status", "CreatedOffline", "IssuedAt", "CreatedAt")
                        VALUES (invoice_id, 'INV-' || UPPER(rec.scope_code) || LPAD(seq::text, GREATEST(5, LENGTH(seq::text)), '0'),
                            rec.stop_id, rec.trip_id, rec.customer_id, billed, paid,
                            CASE WHEN invoice_status = 'Voided' THEN 0 ELSE billed - paid END,
                            invoice_status, false, rec.created_at, NOW());
                    END LOOP;
                END $$;

                -- Active invoices now include both planned and recorded items. Completed
                -- invoice totals and all previously recorded paid amounts remain unchanged.
                UPDATE "SaleInvoices" i SET "TotalAmount" = totals.amount,
                    "Balance" = totals.amount - i."TotalPaid",
                    "Status" = CASE WHEN totals.amount > 0 AND i."TotalPaid" >= totals.amount THEN 'Paid'
                        WHEN i."TotalPaid" > 0 THEN 'PartiallyPaid' ELSE 'Issued' END,
                    "UpdatedAt" = NOW()
                FROM (
                    SELECT s."Id" AS stop_id, COALESCE(SUM(ROUND(
                        CASE WHEN p."DeliveredAt" IS NOT NULL OR p."BasicQtyDelivered" IS NOT NULL OR p."PackagingQtyDelivered" IS NOT NULL
                        THEN COALESCE(p."BasicQtyDelivered", 0) * p."BasicUnitPrice"
                            + COALESCE(p."PackagingQtyDelivered", 0) * COALESCE(p."PackagingUnitPrice", 0)
                        ELSE p."PlannedBasicQuantity" * p."BasicUnitPrice"
                            + COALESCE(p."PlannedPackagingQuantity", 0) * COALESCE(p."PackagingUnitPrice", 0) END, 2)), 0) AS amount
                    FROM "TrekkingTripStops" s JOIN "TrekkingTrips" t ON t."Id" = s."TrekkingTripId"
                    LEFT JOIN "TrekkingTripStopProducts" p ON p."TrekkingTripStopId" = s."Id"
                    WHERE t."Status" IN ('Draft', 'Scheduled', 'InProgress') GROUP BY s."Id"
                ) totals
                WHERE i."TrekkingTripStopId" = totals.stop_id AND i."Status" <> 'Voided';

                -- Import historical amounts, not invented receipt events. Preserve known methods
                -- when the line totals agree with the persisted invoice; otherwise keep its total.
                WITH line_payments AS (
                    SELECT i."Id" AS invoice_id, p."PaymentMethod" AS method, SUM(p."AmtPaid") AS amount
                    FROM "SaleInvoices" i JOIN "TrekkingTripStopProducts" p ON p."TrekkingTripStopId" = i."TrekkingTripStopId"
                    WHERE p."AmtPaid" > 0 GROUP BY i."Id", p."PaymentMethod"
                ), sources AS (
                    SELECT i."Id", lp.method, lp.amount
                    FROM "SaleInvoices" i JOIN line_payments lp ON lp.invoice_id = i."Id"
                    WHERE i."TotalPaid" > 0 AND i."TotalPaid" = (SELECT SUM(x.amount) FROM line_payments x WHERE x.invoice_id = i."Id")
                    UNION ALL
                    SELECT i."Id", NULL::text, i."TotalPaid" FROM "SaleInvoices" i
                    WHERE i."TotalPaid" > 0 AND i."TotalPaid" <> COALESCE((SELECT SUM(x.amount) FROM line_payments x WHERE x.invoice_id = i."Id"), 0)
                )
                INSERT INTO "InvoicePayments" ("Id", "SaleInvoiceId", "ClientGeneratedId", "ReceiptNumber", "Amount", "PaymentMethod",
                    "Reference", "RecordedByStaffId", "RecordedAt", "CreatedAt", "InvoiceTotalAtPayment", "BalanceAfterPayment", "IsLegacy")
                SELECT gen_random_uuid(), i."Id", gen_random_uuid(), NULL, src.amount, src.method,
                    'Imported pre-migration payment total; original receipt/time unavailable', COALESCE(t."SalesStaffId", t."DriverStaffId"),
                    COALESCE(i."UpdatedAt", i."IssuedAt"), NOW(), i."TotalAmount", i."Balance", true
                FROM sources src JOIN "SaleInvoices" i ON i."Id" = src."Id" JOIN "TrekkingTrips" t ON t."Id" = i."TrekkingTripId";

                -- Replace only the old automatic sale/payment summaries. Keep manual
                -- adjustments and approved-return credits intact.
                DELETE FROM "CustomerLedgerEntries" e
                WHERE e."IsAutoGenerated" AND EXISTS (
                    SELECT 1 FROM "SaleInvoices" i WHERE i."TrekkingTripStopId" = e."TrekkingTripStopId")
                  AND (e."Description" LIKE 'Invoice % — Goods delivered%'
                    OR e."Description" LIKE 'Goods delivered — Trek %'
                    OR e."Description" LIKE 'Invoice % — Payment received:%'
                    OR e."Description" LIKE 'Payment received (%) — Trek %');

                INSERT INTO "CustomerLedgerEntries" ("Id", "CustomerAccountId", "EntryType", "Amount", "Description", "TrekkingTripId",
                    "TrekkingTripStopId", "PaymentMethod", "IsAutoGenerated", "RecordedAt", "CreatedByStaffId", "CreatedAt", "SaleInvoiceId", "InvoicePaymentId")
                SELECT gen_random_uuid(), i."CustomerAccountId", 'Debit', i."TotalAmount", 'Invoice ' || i."InvoiceNumber", i."TrekkingTripId",
                    i."TrekkingTripStopId", NULL, true, i."IssuedAt", COALESCE(t."SalesStaffId", t."DriverStaffId"), NOW(), i."Id", NULL
                FROM "SaleInvoices" i JOIN "TrekkingTrips" t ON t."Id" = i."TrekkingTripId"
                WHERE i."Status" <> 'Voided' AND i."TotalAmount" > 0;

                INSERT INTO "CustomerLedgerEntries" ("Id", "CustomerAccountId", "EntryType", "Amount", "Description", "TrekkingTripId",
                    "TrekkingTripStopId", "PaymentMethod", "IsAutoGenerated", "RecordedAt", "CreatedByStaffId", "CreatedAt", "SaleInvoiceId", "InvoicePaymentId")
                SELECT gen_random_uuid(), i."CustomerAccountId", 'Credit', p."Amount", 'Imported payment — Invoice ' || i."InvoiceNumber", i."TrekkingTripId",
                    i."TrekkingTripStopId", p."PaymentMethod", true, p."RecordedAt", p."RecordedByStaffId", NOW(), i."Id", p."Id"
                FROM "InvoicePayments" p JOIN "SaleInvoices" i ON i."Id" = p."SaleInvoiceId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerLedgerEntries_InvoicePayments_InvoicePaymentId",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerLedgerEntries_SaleInvoices_SaleInvoiceId",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropTable(
                name: "InvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerLedgerEntries_InvoicePaymentId",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerLedgerEntries_SaleInvoiceId",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "TrekkingTripStopProducts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "InvoicePaymentId",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SaleInvoiceId",
                table: "CustomerLedgerEntries");
        }
    }
}
