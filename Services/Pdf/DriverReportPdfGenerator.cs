using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using prohpharmacy_trekking_app.Features.Trekking;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class DriverReportPdfGenerator
{
    private const string PrimaryColor = "#0d9488";
    private const string AccentLine   = "#99f6e4";
    private const string TextColor    = "#334155";
    private const string LabelColor   = "#64748b";
    private const string MutedText    = "#94a3b8";
    private const string BorderColor  = "#e2e8f0";
    private const string CardBg       = "#f0fdfa";
    private const string CardBorder   = "#ccfbf1";

    public static byte[] Generate(GetDriverTrekReport.TrekReportData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(7.5f).FontFamily("Helvetica Neue").FontColor(TextColor));

                page.Header().ShowOnce().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    // ── Header ─────────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer container, GetDriverTrekReport.TrekReportData data)
    {
        container.PaddingBottom(6).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(brand =>
                {
                    brand.Spacing(2);
                    brand.Item().Text("PROH PHARMACY").FontSize(11f).SemiBold().FontColor("#1e293b");
                    brand.Item().Text("Address: Logistics Operations Center, Accra").FontSize(6.8f).FontColor(LabelColor);
                    brand.Item().Text($"Region: {data.RegionName}").FontSize(6.8f).FontColor(LabelColor);
                    brand.Item().Text("Mail: info@prohpharmacy.com  •  Phone: +233 53 474 0592").FontSize(6.8f).FontColor(LabelColor);
                });

                row.ConstantItem(90).AlignRight().Column(title =>
                {
                    title.Item().AlignRight().Text("Trek").FontSize(18f).SemiBold().FontColor(PrimaryColor);
                    title.Item().AlignRight().Text("Report").FontSize(18f).SemiBold().FontColor(PrimaryColor);
                });
            });

            col.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(AccentLine);

            col.Item().PaddingTop(6)
                .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                .PaddingVertical(5).PaddingHorizontal(8)
                .Column(card =>
                {
                    card.Spacing(3);
                    InfoRow(card, "Trek No.:", data.TrekNumber, 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Date:", data.ScheduledDate.ToString("dd MMM yyyy"), 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Status:", data.Status, 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Driver:", data.DriverName, 90);
                    if (!string.IsNullOrWhiteSpace(data.SalesStaffName))
                    {
                        card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                        InfoRow(card, "Sales Rep:", data.SalesStaffName, 90);
                    }
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Vehicle:", data.VehicleDisplayName, 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Stops:", $"{data.Summary.StopsVisited} of {data.Summary.TotalStops} visited", 90);
                });
        });
    }

    // ── Content ────────────────────────────────────────────────────────────────

    private static void ComposeContent(IContainer container, GetDriverTrekReport.TrekReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Element(c => ComposeSummaryCards(c, data.Summary));

            if (data.CollectionsByMethod.Count > 0)
                col.Item().Element(c => ComposeCollectionsTable(c, data.CollectionsByMethod));

            if (data.Stops.Count > 0)
                col.Item().Element(c => ComposeStopsTable(c, data.Stops));

            if (data.Refunds.Count > 0)
                col.Item().Element(c => ComposeRefundsTable(c, data.Refunds));

            if (data.StockSummary.Count > 0)
                col.Item().Element(c => ComposeStockTable(c, data.StockSummary));
        });
    }

    // ── Summary cards ──────────────────────────────────────────────────────────

    private static void ComposeSummaryCards(IContainer container, GetDriverTrekReport.ReportSummary s)
    {
        container.Column(col =>
        {
            col.Spacing(6);

            // Block 1 — Trek P&L (what the trek earned)
            col.Item().Text("Trek P&L").FontSize(8f).SemiBold().FontColor(TextColor);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });
                SummaryCard(table, "Total Sales Value",   $"GHS {s.TotalSalesValue:0.00}",      PrimaryColor);
                SummaryCard(table, "Approved Refunds",    FormatDeduction(s.TotalApprovedRefunds), DeductionColor(s.TotalApprovedRefunds),
                    sub: $"{s.ApprovedRefundCount} item(s)");
                SummaryCard(table, "Trek Net Sales",      $"GHS {s.TrekNetSales:0.00}",         "#1e293b");
            });

            // Block 2 — Money Position (what's where right now)
            col.Item().PaddingTop(2).Text("Money Position").FontSize(8f).SemiBold().FontColor(TextColor);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });
                SummaryCard(table, "Cash Collected",         $"GHS {s.CashCollected:0.00}",        "#15803d");
                SummaryCard(table, "Cash Refunds Paid Out",  FormatDeduction(s.CashRefundsPaidOut), DeductionColor(s.CashRefundsPaidOut),
                    sub: "approved + pending");
                SummaryCard(table, "Physical Cash in Hand",  $"GHS {s.PhysicalCashInHand:0.00}",   "#1e293b");
                SummaryCard(table, "Mobile Money Balance",   $"GHS {s.MobileMoneyBalance:0.00}",   "#1e293b",
                    sub: $"collected GHS {s.MobileMoneyCollected:0.00}");
            });

            // Block 3 — Deferred / Exposure
            col.Item().PaddingTop(2).Text("Deferred & Exposure").FontSize(8f).SemiBold().FontColor(TextColor);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });
                SummaryCard(table, "Outstanding (Customer Debt)", $"GHS {s.TotalOutstanding:0.00}",     "#b45309");
                SummaryCard(table, "Pending Refunds",             $"GHS {s.TotalPendingRefunds:0.00}",  "#b45309",
                    sub: $"{s.PendingRefundCount} awaiting approval");
                SummaryCard(table, "Rejected Refunds",            $"GHS {s.TotalRejectedRefunds:0.00}", MutedText,
                    sub: $"{s.RejectedRefundCount} item(s) — informational");
            });
        });
    }

    private static string FormatDeduction(decimal value) =>
        value > 0 ? $"(GHS {value:0.00})" : $"GHS {value:0.00}";

    private static string DeductionColor(decimal value) =>
        value > 0 ? "#b91c1c" : TextColor;

    private static void SummaryCard(TableDescriptor table, string label, string value, string valueColor, string? sub = null)
    {
        table.Cell()
            .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
            .Padding(8)
            .Column(c =>
            {
                c.Item().Text(label).FontSize(6.5f).FontColor(LabelColor);
                c.Item().PaddingTop(3).Text(value).FontSize(9.5f).SemiBold().FontColor(valueColor);
                if (!string.IsNullOrEmpty(sub))
                    c.Item().PaddingTop(2).Text(sub).FontSize(6f).FontColor(MutedText);
            });
    }

    // ── Collections by method ──────────────────────────────────────────────────

    private static void ComposeCollectionsTable(IContainer container, List<GetDriverTrekReport.CollectionByMethod> collections)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Collections by Payment Method").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3f);
                    cols.RelativeColumn(2f);
                });

                HeaderCell(table, "Payment Method");
                HeaderCell(table, "Amount Collected", alignCenter: true);

                var i = 1;
                foreach (var c in collections)
                {
                    var bg = i++ % 2 == 1 ? CardBg : "#f8fbf9";
                    BodyCell(table, c.Method, bg);
                    BodyCell(table, $"GHS {c.Amount:0.00}", bg, alignCenter: true);
                }
            });
        });
    }

    // ── Stops table ────────────────────────────────────────────────────────────

    private static void ComposeStopsTable(IContainer container, List<GetDriverTrekReport.StopReport> stops)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Stop Breakdown").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(18);
                    cols.RelativeColumn(3f);
                    cols.RelativeColumn(2f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(2f);
                });

                HeaderCell(table, "#",               alignCenter: true);
                HeaderCell(table, "Customer");
                HeaderCell(table, "Invoice");
                HeaderCell(table, "Amount Due",      alignCenter: true);
                HeaderCell(table, "Collected",       alignCenter: true);
                HeaderCell(table, "Balance",         alignCenter: true);
                HeaderCell(table, "Payment Method",  alignCenter: true);

                var i = 1;
                foreach (var stop in stops)
                {
                    var bg = i++ % 2 == 1 ? CardBg : "#f8fbf9";
                    BodyCell(table, stop.Sequence.ToString(), bg, alignCenter: true);
                    BodyCell(table, stop.CustomerName, bg);
                    BodyCell(table, stop.InvoiceNumber ?? "—", bg);
                    BodyCell(table, $"GHS {stop.AmountDue:0.00}", bg, alignCenter: true);
                    BodyCell(table, $"GHS {stop.AmtPaid:0.00}", bg, alignCenter: true);
                    BodyCell(table, stop.Balance > 0 ? $"GHS {stop.Balance:0.00}" : "—", bg, alignCenter: true, color: stop.Balance > 0 ? "#b45309" : null);
                    BodyCell(table, stop.PaymentMethods.Count > 0 ? string.Join(" · ", stop.PaymentMethods) : "—", bg, alignCenter: true);
                }
            });
        });
    }

    // ── Refunds table ──────────────────────────────────────────────────────────

    private static void ComposeRefundsTable(IContainer container, List<GetDriverTrekReport.RefundLineItem> refunds)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Refunds").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(18);        // #
                    cols.RelativeColumn(2.5f);      // Customer
                    cols.RelativeColumn(1.6f);      // Invoice
                    cols.RelativeColumn(2.5f);      // Product
                    cols.RelativeColumn(1f);        // Basic Qty
                    cols.RelativeColumn(1.6f);      // Amount
                    cols.RelativeColumn(1.3f);      // Method
                    cols.RelativeColumn(1.3f);      // Status
                    cols.RelativeColumn(2.2f);      // Reason / Recorded
                });

                HeaderCell(table, "#",         alignCenter: true);
                HeaderCell(table, "Customer");
                HeaderCell(table, "Invoice");
                HeaderCell(table, "Product");
                HeaderCell(table, "Basic Qty", alignCenter: true);
                HeaderCell(table, "Amount",    alignCenter: true);
                HeaderCell(table, "Method",    alignCenter: true);
                HeaderCell(table, "Status",    alignCenter: true);
                HeaderCell(table, "Reason / Recorded");

                var i = 1;
                foreach (var r in refunds)
                {
                    var bg = i++ % 2 == 1 ? CardBg : "#f8fbf9";
                    var statusColor = r.ApprovalStatus switch
                    {
                        "Approved" => "#15803d",
                        "Pending"  => "#b45309",
                        "Rejected" => "#b91c1c",
                        _          => TextColor
                    };
                    var reasonLine = string.IsNullOrWhiteSpace(r.Reason) ? "—" : r.Reason;
                    var recordedLine = $"{r.RecordedAt:dd MMM yyyy HH:mm} UTC";

                    BodyCell(table, r.Sequence > 0 ? r.Sequence.ToString() : "—", bg, alignCenter: true);
                    BodyCell(table, r.CustomerName, bg);
                    BodyCell(table, r.InvoiceNumber ?? "—", bg);
                    BodyCell(table, r.ProductName, bg);
                    BodyCell(table, $"{r.BasicQtyReturned:0.###}", bg, alignCenter: true);
                    BodyCell(table, r.RefundAmount.HasValue ? $"GHS {r.RefundAmount.Value:0.00}" : "—", bg, alignCenter: true);
                    BodyCell(table, r.RefundMethod ?? "—", bg, alignCenter: true);
                    BodyCell(table, r.ApprovalStatus, bg, alignCenter: true, color: statusColor);

                    table.Cell()
                        .Background(bg).BorderRight(0.5f).BorderBottom(0.5f).BorderColor("#ffffff")
                        .MinHeight(18).PaddingVertical(3).PaddingHorizontal(4).AlignMiddle()
                        .Column(c =>
                        {
                            c.Item().Text(reasonLine).FontSize(7.5f).FontColor(TextColor);
                            c.Item().Text(recordedLine).FontSize(6.3f).FontColor(MutedText);
                            if (r.ApprovalStatus == "Rejected" && !string.IsNullOrWhiteSpace(r.RejectionReason))
                                c.Item().Text($"Rejected: {r.RejectionReason}").FontSize(6.3f).FontColor("#b91c1c");
                        });
                }
            });
        });
    }

    // ── Stock summary ──────────────────────────────────────────────────────────

    private static void ComposeStockTable(IContainer container, List<GetDriverTrekReport.StockSummaryItem> stock)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Stock Reconciliation").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                });

                HeaderCell(table, "Product");
                HeaderCell(table, "Loaded",    alignCenter: true);
                HeaderCell(table, "Delivered", alignCenter: true);
                HeaderCell(table, "Returned",  alignCenter: true);
                HeaderCell(table, "Remaining", alignCenter: true);

                var i = 1;
                foreach (var item in stock)
                {
                    var bg = i++ % 2 == 1 ? CardBg : "#f8fbf9";
                    BodyCell(table, item.ProductName, bg);
                    BodyCell(table, $"{item.BasicQtyLoaded:0.###}", bg, alignCenter: true);
                    BodyCell(table, $"{item.BasicQtyDelivered:0.###}", bg, alignCenter: true);
                    BodyCell(table, item.BasicQtyApprovedReturns > 0 ? $"{item.BasicQtyApprovedReturns:0.###}" : "—", bg, alignCenter: true);
                    BodyCell(table, $"{item.BasicQtyRemaining:0.###}", bg, alignCenter: true, color: item.BasicQtyRemaining == 0 ? MutedText : null);
                }
            });
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void InfoRow(ColumnDescriptor col, string label, string value, int labelWidth = 60)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(labelWidth).Text(label).FontSize(7f).Medium().FontColor(TextColor);
            r.RelativeItem().Text(value).FontSize(7f).FontColor(LabelColor);
        });
    }

    private static void HeaderCell(TableDescriptor table, string text, bool alignCenter = false)
    {
        var cell = table.Cell()
            .Background(PrimaryColor).BorderRight(0.5f).BorderColor("#ccefe8")
            .MinHeight(20).PaddingVertical(4).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(Colors.White); return; }
        cell.Text(text).FontSize(7.5f).FontColor(Colors.White);
    }

    private static void BodyCell(TableDescriptor table, string text, string background, bool alignCenter = false, string? color = null)
    {
        var textColor = color ?? TextColor;
        var cell = table.Cell()
            .Background(background).BorderRight(0.5f).BorderBottom(0.5f).BorderColor("#ffffff")
            .MinHeight(18).PaddingVertical(3).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(textColor); return; }
        cell.Text(text).FontSize(7.5f).FontColor(textColor);
    }

    private static void ComposeFooter(IContainer container)
    {
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem()
                .Text("Proh Pharmacy Logistics Management System")
                .FontSize(5.8f).FontColor(MutedText);

            row.ConstantItem(180).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(5.8f).FontColor(MutedText);
                text.CurrentPageNumber().FontSize(5.8f).FontColor(MutedText);
                text.Span(" of ").FontSize(5.8f).FontColor(MutedText);
                text.TotalPages().FontSize(5.8f).FontColor(MutedText);
                text.Span($"  •  {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(5.8f).FontColor(MutedText);
            });
        });
    }
}
