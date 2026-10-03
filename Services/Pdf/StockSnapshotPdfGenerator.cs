using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using prohpharmacy_trekking_app.Features.Trekking;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class StockSnapshotPdfGenerator
{
    private const string PrimaryColor = "#0d9488";
    private const string AccentLine   = "#99f6e4";
    private const string TextColor    = "#334155";
    private const string LabelColor   = "#64748b";
    private const string MutedText    = "#94a3b8";
    private const string BorderColor  = "#e2e8f0";
    private const string CardBg       = "#f0fdfa";
    private const string CardBorder   = "#ccfbf1";
    private const string AlertColor   = "#b91c1c";

    public static byte[] Generate(GetTrekStockSnapshot.Response data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(7.5f).FontFamily("Helvetica Neue").FontColor(TextColor));

                page.Header().ShowOnce().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, GetTrekStockSnapshot.Response data)
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
                    brand.Item().Text("Mail: info@prohpharmacy.com  •  Phone: +233 53 474 0592").FontSize(6.8f).FontColor(LabelColor);
                });

                row.ConstantItem(160).AlignRight().Column(title =>
                {
                    title.Item().AlignRight().Text("Stock").FontSize(18f).SemiBold().FontColor(PrimaryColor);
                    title.Item().AlignRight().Text("Reconciliation").FontSize(18f).SemiBold().FontColor(PrimaryColor);
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
                    InfoRow(card, "Date:", data.TrekDate.ToString("dd MMM yyyy"), 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Status:", data.TrekStatus, 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Captured:", data.CapturedAt?.ToString("dd MMM yyyy HH:mm 'UTC'") ?? "—", 90);
                });
        });
    }

    private static void ComposeContent(IContainer container, GetTrekStockSnapshot.Response data)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Element(c => ComposeSummaryCards(c, data.Totals));

            if (data.Items.Count > 0)
                col.Item().Element(c => ComposeSnapshotTable(c, data.Items));
            else
                col.Item().PaddingTop(10).AlignCenter()
                    .Text("No snapshot rows recorded for this trek.")
                    .FontSize(8f).FontColor(MutedText);
        });
    }

    private static void ComposeSummaryCards(IContainer container, GetTrekStockSnapshot.SnapshotTotals totals)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("Overview").FontSize(8f).SemiBold().FontColor(TextColor);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });
                SummaryCard(table, "Products Tracked", totals.ProductCount.ToString(), PrimaryColor);
                SummaryCard(table, "Total Revenue", $"GHS {totals.TotalRevenue:0.00}", "#1e293b");
                SummaryCard(table, "Discrepancies",
                    totals.DiscrepancyCount.ToString(),
                    totals.DiscrepancyCount > 0 ? AlertColor : TextColor,
                    sub: totals.DiscrepancyCount > 0 ? "rows with sold > start" : "no issues flagged");
            });
        });
    }

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

    private static void ComposeSnapshotTable(IContainer container, List<GetTrekStockSnapshot.SnapshotItem> items)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Per-Product Reconciliation").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3f);        // Product
                    cols.RelativeColumn(2f);        // Start
                    cols.RelativeColumn(2f);        // Sold
                    cols.RelativeColumn(2f);        // Remain
                    cols.RelativeColumn(1.6f);      // Revenue
                });

                HeaderCell(table, "Product");
                HeaderCell(table, "Opening Qty", alignCenter: true);
                HeaderCell(table, "Sold Qty", alignCenter: true);
                HeaderCell(table, "Balance Qty", alignCenter: true);
                HeaderCell(table, "Revenue", alignCenter: true);

                var i = 1;
                foreach (var item in items)
                {
                    var bg = i++ % 2 == 1 ? CardBg : "#f8fbf9";
                    var remainColor = (item.BasicQtyRemaining < 0 || item.PackagingQtyRemaining < 0) ? AlertColor : null;

                    BodyCell(table, item.ProductName, bg);
                    BodyCell(table, FormatQty(item.PackagingQtyAtStart, item.PackagingUnitName, item.BasicQtyAtStart, item.BasicUnitName), bg, alignCenter: true);
                    BodyCell(table, FormatQty(item.PackagingQtySold, item.PackagingUnitName, item.BasicQtySold, item.BasicUnitName), bg, alignCenter: true);
                    BodyCell(table, FormatQty(item.PackagingQtyRemaining, item.PackagingUnitName, item.BasicQtyRemaining, item.BasicUnitName), bg, alignCenter: true, color: remainColor);
                    BodyCell(table, $"GHS {item.RevenueAmount:0.00}", bg, alignCenter: true);
                }
            });

            col.Item().PaddingTop(4).Text("Red balance = sold beyond opening stock")
                .FontSize(6f).FontColor(MutedText);
        });
    }

    private static string FormatQty(decimal pkgQty, string? pkgUnit, decimal basicQty, string? basicUnit)
    {
        var hasPkg = !string.IsNullOrWhiteSpace(pkgUnit) && pkgQty != 0;
        var basicLabel = string.IsNullOrWhiteSpace(basicUnit) ? "" : $" {basicUnit}";
        var basicPart = $"{basicQty:0.###}{basicLabel}";
        if (!hasPkg) return basicPart;
        return $"{pkgQty:0.###} {pkgUnit}  ·  {basicPart}";
    }

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
