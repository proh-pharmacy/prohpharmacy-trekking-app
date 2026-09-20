using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class TrekkingSheetPdfGenerator
{
    private const string PrimaryColor = "#0d9488";
    private const string AccentLine   = "#99f6e4";
    private const string TextColor    = "#334155";
    private const string LabelColor   = "#64748b";
    private const string MutedText    = "#94a3b8";
    private const string BorderColor  = "#e2e8f0";
    private const string CardBg       = "#f0fdfa";
    private const string CardBorder   = "#ccfbf1";
    private const string AltRowBg     = "#f8fafc";

    static TrekkingSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public class TrekkingSheetData
    {
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string? SalesStaffName { get; set; }
        public string VehicleDisplayName { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public Guid? DriverToken { get; set; }
        public string? FrontendUrl { get; set; }
        public List<StopData> Stops { get; set; } = [];

        public class StopData
        {
            public int Sequence { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerCode { get; set; } = string.Empty;
            public string? PrimaryPhoneNumber { get; set; }
            public string? DistrictName { get; set; }
            public string? PrimaryLocationLandmark { get; set; }
            public string? PrimaryLocationStreet { get; set; }
            public List<ProductData> Products { get; set; } = [];
            public List<ReturnData> Returns { get; set; } = [];
        }

        public class ProductData
        {
            public string ProductName { get; set; } = string.Empty;
            public string? BasicUnitName { get; set; }
            public string? PackagingUnitName { get; set; }
            public decimal PlannedBasicQuantity { get; set; }
            public decimal? PlannedPackagingQuantity { get; set; }
            public decimal? BasicQtyDelivered { get; set; }
            public decimal? PackagingQtyDelivered { get; set; }
            public string? PaymentMethod { get; set; }
            public decimal? AmtPaid { get; set; }
            public decimal? Balance { get; set; }
            public string? Notes { get; set; }
        }

        public class ReturnData
        {
            public string ProductName { get; set; } = string.Empty;
            public string? BasicUnitName { get; set; }
            public string? PackagingUnitName { get; set; }
            public decimal BasicUnitPrice { get; set; }
            public decimal? PackagingUnitPrice { get; set; }
            public decimal BasicQtyReturned { get; set; }
            public decimal? PackagingQtyReturned { get; set; }
            public decimal? RefundAmount { get; set; }
            public string? RefundMethod { get; set; }
            public string? Reason { get; set; }
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

    private static byte[]? GenerateQrCode(TrekkingSheetData data)
    {
        if (data.DriverToken is null || string.IsNullOrWhiteSpace(data.FrontendUrl))
            return null;

        var url = $"{data.FrontendUrl.TrimEnd('/')}/treks/driver?token={data.DriverToken}&source=qr";
        using var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var pngCode = new PngByteQRCode(qrData);
        return pngCode.GetGraphic(5);
    }

    private static void ComposeHeader(IContainer container, TrekkingSheetData data)
    {
        var qrBytes = GenerateQrCode(data);

        container.PaddingBottom(6).Column(col =>
        {
            col.Item().Row(row =>
            {
                // Left: company branding
                row.RelativeItem().Column(brandCol =>
                {
                    brandCol.Spacing(2);

                    brandCol.Item()
                        .Text("PROH PHARMACY")
                        .FontSize(11f).SemiBold().FontColor("#1e293b");

                    brandCol.Item()
                        .Text("Address: Logistics Operations Center, Accra")
                        .FontSize(6.8f).FontColor(LabelColor);

                    if (!string.IsNullOrWhiteSpace(data.RegionName))
                        brandCol.Item()
                            .Text($"Region: {data.RegionName}")
                            .FontSize(6.8f).FontColor(LabelColor);

                    brandCol.Item()
                        .Text("Mail: info@prohpharmacy.com  •  Phone: +233 53 474 0592")
                        .FontSize(6.8f).FontColor(LabelColor);
                });

                // Right: QR code + "Trekking Sheet" label
                row.ConstantItem(90).AlignRight().Column(titleCol =>
                {
                    if (qrBytes is not null)
                    {
                        titleCol.Item().AlignRight().Width(65).Image(qrBytes);
                        titleCol.Item().PaddingTop(3).AlignRight()
                            .Text("Trekking Sheet")
                            .FontSize(7.5f).SemiBold().FontColor(PrimaryColor);
                    }
                    else
                    {
                        titleCol.Item().AlignRight()
                            .Text("Trekking")
                            .FontSize(18f).SemiBold().FontColor(PrimaryColor);
                        titleCol.Item().AlignRight()
                            .Text("Sheet")
                            .FontSize(18f).SemiBold().FontColor(PrimaryColor);
                    }
                });
            });

            col.Item().PaddingTop(6).LineHorizontal(0.8f).LineColor(AccentLine);

            col.Item().PaddingTop(6).Column(detailsCol =>
            {
                detailsCol.Spacing(4);
                detailsCol.Item()
                    .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                    .PaddingVertical(5).PaddingHorizontal(8)
                    .Column(card =>
                    {
                        card.Spacing(3);
                        InfoRow(card, "Trek No.:", data.TrekNumber, 90);
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
                        InfoRow(card, "Total Stops:", $"{data.Stops.Count} {(data.Stops.Count == 1 ? "Stop" : "Stops")}", 90);
                        card.Item().LineHorizontal(0.5f).LineColor(BorderColor);
                        InfoRow(card, "Scheduled Date:", data.ScheduledDate.ToString("dd MMM yyyy"), 90);
                    });
            });
        });
    }

    private static void InfoRow(ColumnDescriptor col, string label, string value, int labelWidth = 60)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(labelWidth).Text(label).FontSize(7f).Medium().FontColor(TextColor);
            r.RelativeItem().Text(value).FontSize(7f).FontColor(LabelColor);
        });
    }

    private static void ComposeContent(IContainer container, TrekkingSheetData data)
    {
        container.Column(col =>
        {
            col.Spacing(0);

            var sortedStops = data.Stops.OrderBy(s => s.Sequence).ToList();

            if (sortedStops.Count == 0)
            {
                col.Item()
                    .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                    .Padding(16).AlignCenter()
                    .Text("No stops assigned to this trekking sheet.")
                    .FontSize(8f).FontColor(MutedText);
            }
            else
            {
                for (var i = 0; i < sortedStops.Count; i++)
                {
                    col.Item().Element(c => ComposeStop(c, sortedStops[i]));
                    if (i < sortedStops.Count - 1)
                        col.Item().PaddingVertical(4).LineHorizontal(1.5f).LineColor(PrimaryColor);
                }
            }

            col.Item().PaddingTop(12).Element(ComposeSignatureBlock);
        });
    }

    private static void ComposeSignatureBlock(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(5);

            col.Item()
                .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                .PaddingHorizontal(10).PaddingVertical(7)
                .Row(row =>
                {
                    row.RelativeItem(4).Text("Driver Signature: ___________________________").FontSize(6.8f).FontColor(TextColor);
                    row.RelativeItem(4).Text("Supervisor Sign-off: ___________________________").FontSize(6.8f).FontColor(TextColor);
                    row.RelativeItem(3).AlignRight().Text("Date: __________________").FontSize(6.8f).FontColor(TextColor);
                });

            col.Item().Column(termsCol =>
            {
                termsCol.Spacing(1);
                termsCol.Item().Text("Terms & Conditions:").FontSize(6.5f).SemiBold().FontColor(LabelColor);
                termsCol.Item()
                    .Text("All goods and medical supplies must be inspected upon delivery. Delivered quantities, payment receipts, and customer endorsements must be confirmed before departure. Discrepancies must be recorded immediately in the notes column.")
                    .FontSize(5.8f).FontColor(MutedText);
            });
        });
    }

    private static void ComposeStop(IContainer container, TrekkingSheetData.StopData stop)
    {
        container.Column(stopCol =>
        {
            stopCol.Item()
                .Background(CardBg).Border(0.5f).BorderColor(CardBorder)
                .PaddingHorizontal(8).PaddingVertical(6)
                .Row(row =>
                {
                    // Left: Stop+Code, Customer line
                    row.RelativeItem(1).Column(col1 =>
                    {
                        col1.Spacing(3);

                        col1.Item().Row(r =>
                        {
                            r.ConstantItem(75).Text("Stop:").FontSize(7f).Medium().FontColor(TextColor);
                            r.RelativeItem().Text(text =>
                            {
                                text.Span(stop.Sequence.ToString()).FontSize(7f).FontColor(TextColor).SemiBold();
                                if (!string.IsNullOrWhiteSpace(stop.CustomerCode))
                                    text.Span($"  —  {stop.CustomerCode}").FontSize(7f).FontColor(MutedText);
                            });
                        });
                        col1.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        var customerParts = new[] { stop.CustomerName, stop.PrimaryPhoneNumber, stop.DistrictName, stop.PrimaryLocationLandmark, stop.PrimaryLocationStreet }
                            .Where(s => !string.IsNullOrWhiteSpace(s));
                        InfoRow(col1, "Customer:", string.Join(", ", customerParts), 75);
                    });

                    row.ConstantItem(16);

                    // Right: Customer Signature, Date
                    row.RelativeItem(1).Column(col2 =>
                    {
                        col2.Spacing(3);

                        InfoRow(col2, "Customer Signature:", string.Empty, 115);
                        col2.Item().LineHorizontal(0.5f).LineColor(BorderColor);

                        InfoRow(col2, "Date:", string.Empty, 115);
                    });
                });

            // Products table
            stopCol.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(0.4f);  // #
                    columns.RelativeColumn(2.8f);  // Description
                    columns.RelativeColumn(1.2f);  // Planned
                    columns.RelativeColumn(1.2f);  // Delivered
                    columns.RelativeColumn(1.5f);  // Payment Method
                    columns.RelativeColumn(1.0f);  // Amt Paid
                    columns.RelativeColumn(1.0f);  // Balance
                    columns.RelativeColumn(1.5f);  // Notes
                });

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

                if (stop.Products.Count == 0)
                {
                    table.Cell().ColumnSpan(8)
                        .Background("#f8fbf9").BorderBottom(0.5f).BorderColor("#ffffff")
                        .MinHeight(20).PaddingVertical(4).AlignCenter().AlignMiddle()
                        .Text("No products planned for this stop.").FontSize(7.5f).FontColor(MutedText).Italic();
                }
                else
                {
                    var itemIndex = 1;
                    foreach (var product in stop.Products)
                    {
                        var background = itemIndex % 2 == 1 ? "#eef7f3" : "#f8fbf9";

                        var plannedParts = new List<string>();
                        if (product.PlannedBasicQuantity > 0)
                            plannedParts.Add($"{product.PlannedBasicQuantity:0.###}{(string.IsNullOrWhiteSpace(product.BasicUnitName) ? string.Empty : $" {product.BasicUnitName}")}");
                        if (product.PlannedPackagingQuantity.HasValue && product.PlannedPackagingQuantity > 0)
                            plannedParts.Add($"{product.PlannedPackagingQuantity:0.###}{(string.IsNullOrWhiteSpace(product.PackagingUnitName) ? string.Empty : $" {product.PackagingUnitName}")}");

                        var deliveredParts = new List<string>();
                        if (product.BasicQtyDelivered.HasValue)
                            deliveredParts.Add($"{product.BasicQtyDelivered:0.###}{(string.IsNullOrWhiteSpace(product.BasicUnitName) ? string.Empty : $" {product.BasicUnitName}")}");
                        if (product.PackagingQtyDelivered.HasValue)
                            deliveredParts.Add($"{product.PackagingQtyDelivered:0.###}{(string.IsNullOrWhiteSpace(product.PackagingUnitName) ? string.Empty : $" {product.PackagingUnitName}")}");

                        BodyCell(table, itemIndex.ToString(), background, alignCenter: true);
                        BodyCell(table, product.ProductName, background, alignCenter: false);
                        BodyCell(table, string.Join("\n", plannedParts), background, alignCenter: true);
                        BodyCell(table, string.Join("\n", deliveredParts), background, alignCenter: true);
                        BodyCell(table, product.PaymentMethod ?? string.Empty, background, alignCenter: true);
                        BodyCell(table, product.AmtPaid.HasValue ? $"GHS {product.AmtPaid:0.00}" : string.Empty, background, alignCenter: true);
                        BodyCell(table, product.Balance.HasValue ? $"GHS {product.Balance:0.00}" : string.Empty, background, alignCenter: true);
                        BodyCell(table, product.Notes ?? string.Empty, background, alignCenter: false);

                        itemIndex++;
                    }
                }
            });

            if (stop.Returns.Count > 0)
            {
                stopCol.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(0.4f);
                        columns.RelativeColumn(3.0f);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2.5f);
                    });

                    table.Header(header =>
                    {
                        ReturnHeaderCell(header, "#", alignCenter: true);
                        ReturnHeaderCell(header, "Returns — Description", alignCenter: false);
                        ReturnHeaderCell(header, "Qty Returned", alignCenter: true);
                        ReturnHeaderCell(header, "Refund Amount", alignCenter: true);
                        ReturnHeaderCell(header, "Refund Method", alignCenter: true);
                        ReturnHeaderCell(header, "Reason", alignCenter: false);
                    });

                    var returnIndex = 1;
                    foreach (var ret in stop.Returns)
                    {
                        var background = returnIndex % 2 == 1 ? "#fff7f7" : "#fef2f2";

                        var qtyParts = new List<string>();
                        if (ret.BasicQtyReturned > 0)
                            qtyParts.Add($"{ret.BasicQtyReturned:0.###}{(string.IsNullOrWhiteSpace(ret.BasicUnitName) ? string.Empty : $" {ret.BasicUnitName}")}");
                        if (ret.PackagingQtyReturned.HasValue && ret.PackagingQtyReturned > 0)
                            qtyParts.Add($"{ret.PackagingQtyReturned:0.###}{(string.IsNullOrWhiteSpace(ret.PackagingUnitName) ? string.Empty : $" {ret.PackagingUnitName}")}");

                        ReturnBodyCell(table, returnIndex.ToString(), background, alignCenter: true);
                        ReturnBodyCell(table, ret.ProductName, background, alignCenter: false);
                        ReturnBodyCell(table, string.Join("\n", qtyParts), background, alignCenter: true);
                        ReturnBodyCell(table, ret.RefundAmount.HasValue ? $"GHS {ret.RefundAmount:0.00}" : string.Empty, background, alignCenter: true);
                        ReturnBodyCell(table, ret.RefundMethod ?? string.Empty, background, alignCenter: true);
                        ReturnBodyCell(table, ret.Reason ?? string.Empty, background, alignCenter: false);

                        returnIndex++;
                    }
                });
            }
        });
    }

    private static void HeaderCell(TableCellDescriptor table, string text, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background(PrimaryColor).BorderRight(0.5f).BorderColor("#ccefe8")
            .MinHeight(20).PaddingVertical(4).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(Colors.White); return; }
        cell.Text(text).FontSize(7.5f).FontColor(Colors.White);
    }

    private static void BodyCell(TableDescriptor table, string text, string background, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background(background).BorderRight(0.5f).BorderBottom(0.5f).BorderColor("#ffffff")
            .MinHeight(20).PaddingVertical(3).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(TextColor); return; }
        cell.Text(text).FontSize(7.5f).FontColor(TextColor);
    }

    private static void ReturnHeaderCell(TableCellDescriptor table, string text, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background("#fef2f2").BorderRight(0.5f).BorderColor("#fca5a5")
            .MinHeight(18).PaddingVertical(3).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor("#b91c1c"); return; }
        cell.Text(text).FontSize(7.5f).FontColor("#b91c1c");
    }

    private static void ReturnBodyCell(TableDescriptor table, string text, string background, bool alignCenter = true)
    {
        var cell = table.Cell()
            .Background(background).BorderRight(0.5f).BorderBottom(0.5f).BorderColor("#ffffff")
            .MinHeight(18).PaddingVertical(3).PaddingHorizontal(4).AlignMiddle();

        if (alignCenter) { cell.AlignCenter().Text(text).FontSize(7.5f).FontColor(TextColor); return; }
        cell.Text(text).FontSize(7.5f).FontColor(TextColor);
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
