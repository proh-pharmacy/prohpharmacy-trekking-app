using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace prohpharmacy_trekking_app.Services.Pdf;

public static class TrekkingSheetPdfGenerator
{
    private const string PrimaryColor = "#00bf6f";
    private const string DarkSlate = "#1e293b";
    private const string BodyText = "#475569";
    private const string LabelText = "#64748b";
    private const string MutedText = "#94a3b8";
    private const string RuleColor = "#e2e8f0";
    private const string AltRow = "#f8fafc";
    private const string White = "#ffffff";
    private const string StopHeaderBg = "#f0fdf4";

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
            public string? PrimaryLocationLandmark { get; set; }
            public string? PrimaryLocationStreet { get; set; }
            public List<ProductData> Products { get; set; } = [];
        }

        public class ProductData
        {
            public string ProductName { get; set; } = string.Empty;
            public string? Unit { get; set; }
            public decimal PlannedQuantity { get; set; }
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
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica").FontColor(BodyText));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, TrekkingSheetData data)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Spacing(3);

            col.Item().Text("Proh Pharmacy")
                .FontSize(11).Bold().FontColor(DarkSlate);

            col.Item().Text("Trekking Sheet")
                .FontSize(9).FontColor(PrimaryColor);

            col.Item().PaddingTop(4).LineHorizontal(1f).LineColor(PrimaryColor);
            col.Item().PaddingTop(6);

            col.Item().Row(row =>
            {
                row.Spacing(14);
                Field(row.RelativeItem(), "Trek #", data.TrekNumber);
                Field(row.RelativeItem(), "Date", data.ScheduledDate.ToString("dd MMM yyyy"));
                Field(row.RelativeItem(), "Driver", data.DriverName);
                Field(row.RelativeItem(), "Vehicle", data.VehicleDisplayName);
                Field(row.RelativeItem(), "Branch", data.BranchName);
            });

            col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(RuleColor);
        });
    }

    private static void ComposeContent(IContainer container, TrekkingSheetData data)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            foreach (var stop in data.Stops.OrderBy(s => s.Sequence))
            {
                col.Item().Element(c => ComposeStop(c, stop));
            }
        });
    }

    private static void ComposeStop(IContainer container, TrekkingSheetData.StopData stop)
    {
        container.Column(col =>
        {
            col.Spacing(0);

            col.Item()
                .Background(StopHeaderBg)
                .BorderLeft(3f).BorderColor(PrimaryColor)
                .Padding(8)
                .Row(row =>
                {
                    row.RelativeItem().Column(inner =>
                    {
                        inner.Item().Text($"Stop {stop.Sequence}")
                            .FontSize(8).FontColor(LabelText);
                        inner.Item().Text($"{stop.CustomerName}  ({stop.CustomerCode})")
                            .FontSize(9.5f).SemiBold().FontColor(DarkSlate);
                        if (!string.IsNullOrWhiteSpace(stop.PrimaryLocationLandmark))
                        {
                            var locationText = !string.IsNullOrWhiteSpace(stop.PrimaryLocationStreet)
                                ? $"{stop.PrimaryLocationLandmark}, {stop.PrimaryLocationStreet}"
                                : stop.PrimaryLocationLandmark;
                            inner.Item().Text(locationText).FontSize(8).FontColor(BodyText);
                        }
                    });
                });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.5f);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(2f);
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "Product", "Unit", "Qty Planned", "Qty Delivered", "Payment Method", "Amt Paid", "Balance", "Notes" })
                    {
                        header.Cell()
                            .Background(PrimaryColor)
                            .Padding(5)
                            .AlignMiddle()
                            .Text(title)
                            .FontSize(7.5f).Bold().FontColor(Colors.White);
                    }
                });

                var isEven = false;
                foreach (var product in stop.Products)
                {
                    var rowBg = isEven ? AltRow : White;
                    isEven = !isEven;

                    DataCell(table, product.ProductName, rowBg);
                    DataCell(table, product.Unit ?? string.Empty, rowBg);
                    DataCell(table, product.PlannedQuantity.ToString("0.###"), rowBg);
                    DataCell(table, string.Empty, rowBg);
                    DataCell(table, string.Empty, rowBg);
                    DataCell(table, string.Empty, rowBg);
                    DataCell(table, string.Empty, rowBg);
                    DataCell(table, string.Empty, rowBg);
                }

                if (stop.Products.Count == 0)
                {
                    table.Cell().ColumnSpan(8)
                        .Background(AltRow)
                        .BorderBottom(0.5f).BorderColor(RuleColor)
                        .Padding(6)
                        .AlignCenter()
                        .Text("No products planned for this stop.")
                        .FontSize(8).FontColor(MutedText).Italic();
                }
            });
        });
    }

    private static void DataCell(TableDescriptor table, string text, string bg)
    {
        table.Cell()
            .Background(bg)
            .BorderBottom(0.5f).BorderColor(RuleColor)
            .Padding(5)
            .AlignMiddle()
            .Text(text)
            .FontSize(8.5f).FontColor(DarkSlate);
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(RuleColor);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Row(sigs =>
                {
                    sigs.AutoItem().PaddingRight(40)
                        .Text("Driver: ____________")
                        .FontSize(8).FontColor(BodyText);
                    sigs.AutoItem()
                        .Text("Supervisor: ____________")
                        .FontSize(8).FontColor(BodyText);
                });

                row.ConstantItem(200).AlignRight()
                    .Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                    .FontSize(7.5f).FontColor(MutedText);
            });
        });
    }

    private static void Field(IContainer container, string label, string value)
    {
        container.Column(col =>
        {
            col.Spacing(2);
            col.Item().Text(label).FontSize(6.5f).FontColor(LabelText);
            col.Item().Text(value).FontSize(8.5f).FontColor(DarkSlate);
        });
    }
}
