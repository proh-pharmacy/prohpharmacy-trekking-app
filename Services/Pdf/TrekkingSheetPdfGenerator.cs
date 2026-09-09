using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class TrekkingSheetPdfGenerator
{
    // ── Flat Design Color System (Subtle, Modern, Non-Contrasting) ────────────
    // Clean flat teal/emerald theme inspired by modern flat delivery note templates
    private const string PrimaryColor = "#0d9488";      // Flat teal-green (accent title & header)
    private const string AccentLine = "#99f6e4";        // Razor-thin accent rule
    private const string TextColor = "#334155";         // Slate-700: soft, readable body (not harsh black)
    private const string LabelColor = "#64748b";        // Slate-500: elegant subtle gray for metadata labels
    private const string MutedText = "#94a3b8";         // Slate-400: light subtle gray for hints and footers
    private const string BorderColor = "#e2e8f0";       // Slate-200: delicate hairline borders
    private const string CardBg = "#f0fdfa";            // Soft pastel teal/mint card background
    private const string CardBorder = "#ccfbf1";        // Very soft mint card border
    private const string AltRowBg = "#f8fafc";          // Ultra-light slate row

    static TrekkingSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public class TrekkingSheetData
    {
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string VehicleDisplayName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public List<StopData> Stops { get; set; } = [];

        public class StopData
        {
            public int Sequence { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerCode { get; set; } = string.Empty;
            public string? PrimaryPhoneNumber { get; set; }
            public string? DistrictName { get; set; }
            public string? RegionName { get; set; }
            public string? PrimaryLocationLandmark { get; set; }
            public string? PrimaryLocationStreet { get; set; }
            public string? PrimaryContactName { get; set; }
            public string? PrimaryContactPhone { get; set; }
            public List<ProductData> Products { get; set; } = [];
        }

        public class ProductData
        {
            public string ProductName { get; set; } = string.Empty;
            public string? Unit { get; set; }
            public decimal PlannedQuantity { get; set; }
            public decimal? QtyDelivered { get; set; }
            public string? PaymentMethod { get; set; }
            public decimal? AmtPaid { get; set; }
            public decimal? Balance { get; set; }
            public string? Notes { get; set; }
        }
    }

    public static byte[] Generate(TrekkingSheetData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(7.5f).FontFamily("Helvetica Neue").FontColor(TextColor));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    // ── Header (Flat Design: Company on Left, Big Accent Title on Right) ──────

    private static void ComposeHeader(IContainer container, TrekkingSheetData data)
    {
        container.PaddingBottom(6).Column(col =>
        {
            // Top Row: Company Info (Left) and Large Modern Title (Right)
            col.Item().Row(row =>
            {
                // Company branding & contact
                row.RelativeItem().Column(brandCol =>
                {
                    brandCol.Spacing(2);

                    brandCol.Item()
                        .Text("PROH PHARMACY")
                        .FontSize(11f)
                        .SemiBold()
                        .FontColor("#1e293b");

                    brandCol.Item()
                        .Text("Address: Logistics Operations Center, Accra")
                        .FontSize(6.8f)
                        .FontColor(LabelColor);

                    brandCol.Item()
                        .Text("Mail: dispatch@prohpharmacy.com  •  Phone: +233 30 200 0000")
                        .FontSize(6.8f)
                        .FontColor(LabelColor);
                });

                // Title: "Trekking Sheet" in flat accent color
                row.ConstantItem(180).AlignRight().Column(titleCol =>
                {
                    titleCol.Item().AlignRight()
                        .Text("Trekking")
                        .FontSize(18f)
                        .SemiBold()
                        .FontColor(PrimaryColor);

                    titleCol.Item().AlignRight()
                        .Text("Sheet")
                        .FontSize(18f)
                        .SemiBold()
                        .FontColor(PrimaryColor);
                });
            });

            // Clean, razor-thin accent separator
            col.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(AccentLine);

            // Sub-Header: Flat Trip Overview Box (Left) & Schedule Details Box (Right)
            col.Item().PaddingTop(6).Row(row =>
            {
                // Left: Flat Trip Details Card
                row.RelativeItem(1).Column(detailsCol =>
                {
                    detailsCol.Spacing(4);

                    detailsCol.Item()
                        .Text("Trip Details")
                        .FontSize(9.5f)
                        .SemiBold()
                        .FontColor(PrimaryColor);

                    detailsCol.Item()
                        .Background(CardBg)
                        .Border(0.5f).BorderColor(CardBorder)
                        .PaddingVertical(5).PaddingHorizontal(8)
                        .Column(card =>
                        {
                            card.Spacing(3);

                            InfoRow(card, "Driver:", data.DriverName, 55);
                            card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                            InfoRow(card, "Vehicle:", data.VehicleDisplayName, 55);
                            card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                            InfoRow(card, "Branch:", data.BranchName, 55);
                        });
                });

                row.ConstantItem(12); // spacing between columns

                // Right: Schedule Details Card (arranged identically)
                row.RelativeItem(1).Column(scheduleCol =>
                {
                    scheduleCol.Spacing(4);

                    scheduleCol.Item()
                        .Text("Schedule & Trek")
                        .FontSize(9.5f)
                        .SemiBold()
                        .FontColor(PrimaryColor);

                    scheduleCol.Item()
                        .Background(CardBg)
                        .Border(0.5f).BorderColor(CardBorder)
                        .PaddingVertical(5).PaddingHorizontal(8)
                        .Column(card =>
                        {
                            card.Spacing(3);

                            InfoRow(card, "Scheduled Date:", data.ScheduledDate.ToString("dd MMM yyyy"), 80);
                            card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                            InfoRow(card, "Trek Number:", data.TrekNumber, 80);
                            card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                            InfoRow(card, "Total Stops:", $"{data.Stops.Count} {(data.Stops.Count == 1 ? "Stop" : "Stops")}", 80);
                        });
                });
            });
        });
    }

    private static void InfoRow(ColumnDescriptor col, string label, string value, int labelWidth = 60)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(labelWidth)
                .Text(label)
                .FontSize(7f)
                .Medium()
                .FontColor(TextColor);

            r.RelativeItem()
                .Text(value)
                .FontSize(7f)
                .FontColor(LabelColor);
        });
    }

    // ── Content: Stops & Datatables ───────────────────────────────────────────

    private static void ComposeContent(IContainer container, TrekkingSheetData data)
    {
        container.Column(col =>
        {
            col.Spacing(0);

            var sortedStops = data.Stops.OrderBy(s => s.Sequence).ToList();

            if (sortedStops.Count == 0)
            {
                col.Item()
                    .Background(CardBg)
                    .Border(0.5f).BorderColor(CardBorder)
                    .Padding(16)
                    .AlignCenter()
                    .Text("No stops assigned to this trekking sheet.")
                    .FontSize(8f)
                    .FontColor(MutedText);
                return;
            }

            for (var i = 0; i < sortedStops.Count; i++)
            {
                col.Item().Element(c => ComposeStop(c, sortedStops[i]));
                if (i < sortedStops.Count - 1)
                    col.Item().PaddingVertical(4).LineHorizontal(1.5f).LineColor(PrimaryColor);
            }
        });
    }

    private static void ComposeStop(IContainer container, TrekkingSheetData.StopData stop)
    {
        container.Column(stopCol =>
        {
            // Flat Stop Header Card (2-column grid containing stop info, customer signature & date)
            stopCol.Item()
                .Background(CardBg)
                .Border(0.5f).BorderColor(CardBorder)
                .PaddingHorizontal(8).PaddingVertical(6)
                .Row(row =>
                {
                    // Column 1 (Left): Stop, Business Name, Customer Code, District/Region, Address
                    row.RelativeItem(1).Column(col1 =>
                    {
                        col1.Spacing(3);

                        InfoRow(col1, "Stop:", stop.Sequence.ToString(), 75);
                        col1.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col1, "Business Name:", stop.CustomerName, 75);
                        col1.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col1, "Customer Code:", string.IsNullOrWhiteSpace(stop.CustomerCode) ? "—" : stop.CustomerCode, 75);
                        col1.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        var location = string.Join(", ", new[] { stop.DistrictName, stop.RegionName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        InfoRow(col1, "District / Region:", string.IsNullOrWhiteSpace(location) ? "—" : location, 75);
                        col1.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col1, "Address:", string.IsNullOrWhiteSpace(stop.PrimaryLocationStreet) ? "—" : stop.PrimaryLocationStreet, 75);
                    });

                    row.ConstantItem(16);

                    // Column 2 (Right): Tel, Contact, Location, Customer Signature, Date
                    row.RelativeItem(1).Column(col2 =>
                    {
                        col2.Spacing(3);

                        InfoRow(col2, "Tel:", string.IsNullOrWhiteSpace(stop.PrimaryPhoneNumber) ? "—" : stop.PrimaryPhoneNumber, 95);
                        col2.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        var contactLine = string.Join(" / ", new[] { stop.PrimaryContactName, stop.PrimaryContactPhone }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        InfoRow(col2, "Contact:", string.IsNullOrWhiteSpace(contactLine) ? "—" : contactLine, 95);
                        col2.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col2, "Location:", string.IsNullOrWhiteSpace(stop.PrimaryLocationLandmark) ? "—" : stop.PrimaryLocationLandmark, 95);
                        col2.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col2, "Customer Signature:", string.Empty, 95);
                        col2.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col2, "Date:", string.Empty, 95);
                    });
                });

            // Modern Flat Table for this stop (matching StaffAttendancePdfReport design)
            stopCol.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(0.4f);  // #
                    columns.RelativeColumn(3.0f);  // Description
                    columns.RelativeColumn(0.9f);  // Planned
                    columns.RelativeColumn(1.0f);  // Delivered
                    columns.RelativeColumn(1.3f);  // Payment Method
                    columns.RelativeColumn(1.0f);  // Amt Paid
                    columns.RelativeColumn(1.0f);  // Balance
                    columns.RelativeColumn(1.5f);  // Notes
                });

                // Header Row
                table.Header(header =>
                {
                    HeaderCell(header, "#", alignCenter: true);
                    HeaderCell(header, "Description", alignCenter: false);
                    HeaderCell(header, "Planned", alignCenter: true);
                    HeaderCell(header, "Delivered", alignCenter: true);
                    HeaderCell(header, "Payment Method", alignCenter: true);
                    HeaderCell(header, "Amt Paid", alignCenter: true);
                    HeaderCell(header, "Balance", alignCenter: true);
                    HeaderCell(header, "Notes", alignCenter: false);
                });

                // Product Rows
                if (stop.Products.Count == 0)
                {
                    table.Cell().ColumnSpan(8)
                        .Background("#f8fbf9")
                        .BorderBottom(0.5f).BorderColor("#ffffff")
                        .MinHeight(20)
                        .PaddingVertical(4)
                        .AlignCenter()
                        .AlignMiddle()
                        .Text("No products planned for this stop.")
                        .FontSize(7.5f).FontColor(MutedText).Italic();
                }
                else
                {
                    var itemIndex = 1;
                    foreach (var product in stop.Products)
                    {
                        var background = itemIndex % 2 == 1 ? "#eef7f3" : "#f8fbf9";
                        var unitSuffix = string.IsNullOrWhiteSpace(product.Unit) ? string.Empty : $" {product.Unit}";

                        var qtyDelivered = product.QtyDelivered.HasValue
                            ? $"{product.QtyDelivered:0.###}{unitSuffix}" : string.Empty;
                        var amtPaid = product.AmtPaid.HasValue
                            ? $"GHS {product.AmtPaid:0.00}" : string.Empty;
                        var balance = product.Balance.HasValue
                            ? $"GHS {product.Balance:0.00}" : string.Empty;

                        BodyCell(table, itemIndex.ToString(), background, alignCenter: true);
                        BodyCell(table, product.ProductName, background, alignCenter: false);
                        BodyCell(table, $"{product.PlannedQuantity:0.###}{unitSuffix}", background, alignCenter: true);
                        BodyCell(table, qtyDelivered, background, alignCenter: true);
                        BodyCell(table, product.PaymentMethod ?? string.Empty, background, alignCenter: true);
                        BodyCell(table, amtPaid, background, alignCenter: true);
                        BodyCell(table, balance, background, alignCenter: true);
                        BodyCell(table, product.Notes ?? string.Empty, background, alignCenter: false);

                        itemIndex++;
                    }
                }
            });
        });
    }

    private static void HeaderCell(TableCellDescriptor table, string text, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background(PrimaryColor)
            .BorderRight(0.5f)
            .BorderColor("#ccefe8")
            .MinHeight(20)
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .AlignMiddle();

        if (alignCenter)
        {
            cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(Colors.White);
            return;
        }

        cell.Text(text).FontSize(7.5f).FontColor(Colors.White);
    }

    private static void BodyCell(TableDescriptor table, string text, string background, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background(background)
            .BorderRight(0.5f)
            .BorderBottom(0.5f)
            .BorderColor("#ffffff")
            .MinHeight(20)
            .PaddingVertical(3)
            .PaddingHorizontal(4)
            .AlignMiddle();

        if (alignCenter)
        {
            cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(TextColor);
            return;
        }

        cell.Text(text).FontSize(7.5f).FontColor(TextColor);
    }

    // ── Footer: Signature Box & Terms (Directly matching delivery note reference)

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(5);

            // Flat Signature Card (Matching bottom signature card of the reference)
            col.Item()
                .Background(CardBg)
                .Border(0.5f).BorderColor(CardBorder)
                .PaddingHorizontal(10).PaddingVertical(7)
                .Row(row =>
                {
                    row.RelativeItem(4)
                        .Text("Driver Signature: ___________________________")
                        .FontSize(6.8f).FontColor(TextColor);

                    row.RelativeItem(4)
                        .Text("Supervisor Sign-off: ___________________________")
                        .FontSize(6.8f).FontColor(TextColor);

                    row.RelativeItem(3).AlignRight()
                        .Text("Date: __________________")
                        .FontSize(6.8f).FontColor(TextColor);
                });

            // Terms & Conditions note in gentle muted gray
            col.Item().Column(termsCol =>
            {
                termsCol.Spacing(1);

                termsCol.Item()
                    .Text("Terms & Conditions:")
                    .FontSize(6.5f).SemiBold().FontColor(LabelColor);

                termsCol.Item()
                    .Text("All goods and medical supplies must be inspected upon delivery. Delivered quantities, payment receipts, and customer endorsements must be confirmed before departure. Discrepancies must be recorded immediately in the notes column.")
                    .FontSize(5.8f).FontColor(MutedText);
            });

            // Bottom metadata line
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem()
                    .Text("Proh Pharmacy Logistics Management System")
                    .FontSize(5.8f).FontColor(MutedText);

                row.ConstantItem(180).AlignRight()
                    .Text(text =>
                    {
                        text.Span("Page ").FontSize(5.8f).FontColor(MutedText);
                        text.CurrentPageNumber().FontSize(5.8f).FontColor(MutedText);
                        text.Span(" of ").FontSize(5.8f).FontColor(MutedText);
                        text.TotalPages().FontSize(5.8f).FontColor(MutedText);
                        text.Span($"  •  {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(5.8f).FontColor(MutedText);
                    });
            });
        });
    }
}
