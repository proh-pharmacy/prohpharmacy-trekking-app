using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class IssueInvoicesWhenTrekIsScheduled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The preceding invoice-first migration issued invoices for every
            // existing stop, including Draft planning data. Remove only unused
            // Draft invoices so those stops regain the new editable lifecycle.
            // Invoice numbers are intentionally not recycled.
            migrationBuilder.Sql("""
                DELETE FROM "CustomerLedgerEntries" ledger
                USING "SaleInvoices" invoice, "TrekkingTrips" trip
                WHERE ledger."SaleInvoiceId" = invoice."Id"
                  AND invoice."TrekkingTripId" = trip."Id"
                  AND trip."Status" = 'Draft'
                  AND invoice."TotalPaid" = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM "InvoicePayments" payment
                      WHERE payment."SaleInvoiceId" = invoice."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM "TrekkingTripStopReturns" returned
                      WHERE returned."SaleInvoiceId" = invoice."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM "TrekkingTripStopProducts" product
                      WHERE product."TrekkingTripStopId" = invoice."TrekkingTripStopId"
                        AND (product."DeliveredAt" IS NOT NULL
                          OR product."BasicQtyDelivered" IS NOT NULL
                          OR product."PackagingQtyDelivered" IS NOT NULL));

                DELETE FROM "SaleInvoices" invoice
                USING "TrekkingTrips" trip
                WHERE invoice."TrekkingTripId" = trip."Id"
                  AND trip."Status" = 'Draft'
                  AND invoice."TotalPaid" = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM "InvoicePayments" payment
                      WHERE payment."SaleInvoiceId" = invoice."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM "TrekkingTripStopReturns" returned
                      WHERE returned."SaleInvoiceId" = invoice."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM "TrekkingTripStopProducts" product
                      WHERE product."TrekkingTripStopId" = invoice."TrekkingTripStopId"
                        AND (product."DeliveredAt" IS NOT NULL
                          OR product."BasicQtyDelivered" IS NOT NULL
                          OR product."PackagingQtyDelivered" IS NOT NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removed Draft invoices had no financial or delivery activity.
            // Recreating issued documents and reusing their numbers on rollback
            // would be misleading, so this data cleanup is intentionally one-way.
        }
    }
}
