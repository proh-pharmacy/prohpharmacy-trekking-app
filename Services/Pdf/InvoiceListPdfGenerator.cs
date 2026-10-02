using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class InvoiceListPdfGenerator
{
    private const string PrimaryColor = "#0d9488";
    private const string AccentLine   = "#99f6e4";
    private const string TextColor    = "#334155";
    private const string LabelColor   = "#64748b";
    private const string MutedText    = "#94a3b8";
    private const string BorderColor  = "#e2e8f0";
    private const string CardBg       = "#f0fdfa";
    private const string CardBorder   = "#ccfbf1";
    private const string AltRowBg     = "#f8fbf9";

    static InvoiceListPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public class InvoiceListData
    {
        public List<FilterLine> Filters { get; set; } = [];
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public int TotalCount { get; set; }
        public decimal SumTotalAmount { get; set; }
        public decimal SumTotalPaid { get; set; }
        public decimal SumBalance { get; set; }
        public List<Row> Rows { get; set; } = [];

        public class FilterLine
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        public class Row
        {
            public string InvoiceNumber { get; set; } = string.Empty;
            public DateTime IssuedAt { get; set; }
            public string Status { get; set; } = string.Empty;
            public string TrekNumber { get; set; } = string.Empty;
            public DateOnly TrekDate { get; set; }
            public string CustomerCode { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerRegion { get; set; } = string.Empty;
            public decimal TotalAmount { get; set; }
            public decimal TotalPaid { get; set; }
            public decimal Balance { get; set; }
        }
    }

    public static byte[] Generate(InvoiceListData data)
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

    // ── Header ─────────────────────────────────────────────────────────────────

    private static void ComposeHeader(IContainer container, InvoiceListData data)
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

                row.ConstantItem(110).AlignRight().Column(title =>
                {
                    title.Item().AlignRight().Text("Sale").FontSize(18f).SemiBold().FontColor(PrimaryColor);
                    title.Item().AlignRight().Text("Invoices").FontSize(18f).SemiBold().FontColor(PrimaryColor);
                });
            });

            col.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(AccentLine);

            col.Item().PaddingTop(6)
                .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                .PaddingVertical(5).PaddingHorizontal(8)
                .Column(card =>
                {
                    card.Spacing(3);
                    for (int i = 0; i < data.Filters.Count; i++)
                    {
                        if (i > 0) card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                        InfoRow(card, $"{data.Filters[i].Label}:", data.Filters[i].Value, 90);
                    }
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Rows:", data.TotalCount.ToString(), 90);
                    card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                    InfoRow(card, "Generated:", $"{data.GeneratedAtUtc:dd MMM yyyy HH:mm} UTC", 90);
                });
        });
    }

    // ── Content ────────────────────────────────────────────────────────────────

    private static void ComposeContent(IContainer container, InvoiceListData data)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Element(c => ComposeSummaryCards(c, data));
            col.Item().Element(c => ComposeInvoicesTable(c, data.Rows));
        });
    }

    private static void ComposeSummaryCards(IContainer container, InvoiceListData data)
    {
        container.Column(col =>
        {
            col.Spacing(6);

            col.Item().Text("Totals").FontSize(8f).SemiBold().FontColor(TextColor);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });
                SummaryCard(table, "Total Amount", $"GHS {data.SumTotalAmount:0.00}", PrimaryColor);
                SummaryCard(table, "Total Paid",   $"GHS {data.SumTotalPaid:0.00}",   "#15803d");
                SummaryCard(table, "Balance",      $"GHS {data.SumBalance:0.00}",     data.SumBalance > 0 ? "#b91c1c" : "#1e293b");
            });
        });
    }

    private static void SummaryCard(TableDescriptor table, string label, string value, string valueColor)
    {
        table.Cell()
            .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
            .Padding(8)
            .Column(c =>
            {
                c.Item().Text(label).FontSize(6.5f).FontColor(LabelColor);
                c.Item().PaddingTop(3).Text(value).FontSize(9.5f).SemiBold().FontColor(valueColor);
            });
    }

    // ── Invoices table ─────────────────────────────────────────────────────────

    private static void ComposeInvoicesTable(IContainer container, List<InvoiceListData.Row> rows)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("Invoices").FontSize(8f).SemiBold().FontColor(TextColor);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.7f); // Invoice No.
                    cols.RelativeColumn(1.5f); // Issued At
                    cols.RelativeColumn(1.3f); // Status
                    cols.RelativeColumn(1.3f); // Trek No.
                    cols.RelativeColumn(1.3f); // Trek Date
                    cols.RelativeColumn(1.4f); // Customer Code
                    cols.RelativeColumn(2.6f); // Customer Name
                    cols.RelativeColumn(1.8f); // Region
                    cols.RelativeColumn(1.5f); // Total
                    cols.RelativeColumn(1.5f); // Paid
                    cols.RelativeColumn(1.5f); // Balance
                });

                HeaderCell(table, "Invoice No.");
                HeaderCell(table, "Issued At");
                HeaderCell(table, "Status",         alignCenter: true);
                HeaderCell(table, "Trek No.");
                HeaderCell(table, "Trek Date");
                HeaderCell(table, "Customer Code");
                HeaderCell(table, "Customer");
                HeaderCell(table, "Region");
                HeaderCell(table, "Total",          alignCenter: true);
                HeaderCell(table, "Paid",           alignCenter: true);
                HeaderCell(table, "Balance",        alignCenter: true);

                var i = 1;
                foreach (var r in rows)
                {
                    var bg = i++ % 2 == 1 ? CardBg : AltRowBg;
                    var statusColor = r.Status switch
                    {
                        "Paid" => "#15803d",
                        "PartiallyPaid" => "#b45309",
                        "Voided" => "#b91c1c",
                        _ => TextColor
                    };

                    BodyCell(table, r.InvoiceNumber, bg);
                    BodyCell(table, r.IssuedAt.ToString("dd MMM yyyy HH:mm"), bg);
                    BodyCell(table, r.Status, bg, alignCenter: true, color: statusColor);
                    BodyCell(table, r.TrekNumber, bg);
                    BodyCell(table, r.TrekDate == default ? "—" : r.TrekDate.ToString("dd MMM yyyy"), bg);
                    BodyCell(table, r.CustomerCode, bg);
                    BodyCell(table, r.CustomerName, bg);
                    BodyCell(table, r.CustomerRegion, bg);
                    BodyCell(table, $"GHS {r.TotalAmount:0.00}", bg, alignCenter: true);
                    BodyCell(table, $"GHS {r.TotalPaid:0.00}",   bg, alignCenter: true);
                    BodyCell(table, r.Balance > 0 ? $"GHS {r.Balance:0.00}" : "—", bg, alignCenter: true, color: r.Balance > 0 ? "#b45309" : null);
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
