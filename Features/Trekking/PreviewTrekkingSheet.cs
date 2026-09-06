using Carter;
using prohpharmacy_trekking_app.Services.Pdf;

namespace prohpharmacy_trekking_app.Features.Trekking;

public class PreviewTrekkingSheetEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/sheet/preview", () =>
        {
            var data = new TrekkingSheetPdfGenerator.TrekkingSheetData
            {
                TrekNumber = "TRK-00001",
                ScheduledDate = DateOnly.FromDateTime(DateTime.Today),
                DriverName = "Kwame Asante",
                VehicleDisplayName = "Van 1 (GR-1234-24)",
                BranchName = "Tema Branch",
                Stops =
                [
                    new()
                    {
                        Sequence = 1,
                        CustomerName = "Tema Central Pharmacy",
                        CustomerCode = "GAR-00001",
                        PrimaryLocationLandmark = "Opposite the blue mosque, after the junction",
                        PrimaryLocationStreet = "Community 5, Tema",
                        Products =
                        [
                            new() { ProductName = "Paracetamol 500mg", Unit = "Box", PlannedQuantity = 10 },
                            new() { ProductName = "Amoxicillin 250mg", Unit = "Carton", PlannedQuantity = 5 },
                            new() { ProductName = "ORS Sachets", Unit = "Box", PlannedQuantity = 20 }
                        ]
                    },
                    new()
                    {
                        Sequence = 2,
                        CustomerName = "Katamanso Health Store",
                        CustomerCode = "GAR-00002",
                        PrimaryLocationLandmark = "Near the Katamanso police station, red building",
                        PrimaryLocationStreet = "Main Road, Katamanso",
                        Products =
                        [
                            new() { ProductName = "Metronidazole 400mg", Unit = "Box", PlannedQuantity = 8 },
                            new() { ProductName = "Vitamin C 1000mg", Unit = "Bottle", PlannedQuantity = 12 }
                        ]
                    },
                    new()
                    {
                        Sequence = 3,
                        CustomerName = "Ashaiman Pharma Plus",
                        CustomerCode = "GAR-00003",
                        PrimaryLocationLandmark = "Ground floor of the green plaza, beside mobile money booth",
                        PrimaryLocationStreet = "Ashaiman Market Road",
                        Products =
                        [
                            new() { ProductName = "Chloroquine Tablets", Unit = "Pack", PlannedQuantity = 30 },
                            new() { ProductName = "Ibuprofen 400mg", Unit = "Box", PlannedQuantity = 15 },
                            new() { ProductName = "Antacid Suspension", Unit = "Bottle", PlannedQuantity = 6 },
                            new() { ProductName = "Zinc Sulphate", Unit = "Box", PlannedQuantity = 10 }
                        ]
                    }
                ]
            };

            var bytes = TrekkingSheetPdfGenerator.Generate(data);
            return Results.File(bytes, "application/pdf", "TrekkingSheet-Preview.pdf");
        })
        .WithTags("Trekking")
        .WithGroupName(Extensions.SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Preview trekking sheet PDF")
        .WithDescription("Returns a sample trekking sheet PDF with dummy data for design preview purposes.")
        .AllowAnonymous();
    }
}
