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
                RegionName = "Greater Accra Region",
                DriverToken = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
                FrontendUrl = "https://trekking.prohpharmacy.com",
                Stops =
                [
                    new()
                    {
                        Sequence = 1,
                        CustomerName = "Tema Central Pharmacy",
                        CustomerCode = "GAR-00001",
                        PrimaryPhoneNumber = "0244 123 456",
                        DistrictName = "Tema",
                        PrimaryLocationLandmark = "Opposite the blue mosque, after the junction",
                        PrimaryLocationStreet = "Community 5, Tema",
                        Products =
                        [
                            new() { ProductName = "Paracetamol 500mg", BasicUnitName = "Tab", PackagingUnitName = "Box", PlannedBasicQuantity = 10, PlannedPackagingQuantity = 2 },
                            new() { ProductName = "Amoxicillin 250mg", BasicUnitName = "Cap", PackagingUnitName = "Carton", PlannedBasicQuantity = 0, PlannedPackagingQuantity = 5 },
                            new() { ProductName = "ORS Sachets", BasicUnitName = "Sachet", PlannedBasicQuantity = 20 }
                        ]
                    },
                    new()
                    {
                        Sequence = 2,
                        CustomerName = "Katamanso Health Store",
                        CustomerCode = "GAR-00002",
                        PrimaryPhoneNumber = "0201 987 654",
                        DistrictName = "Katamanso",
                        PrimaryLocationLandmark = "Near the Katamanso police station, red building",
                        PrimaryLocationStreet = "Main Road, Katamanso",
                        Products =
                        [
                            new() { ProductName = "Metronidazole 400mg", BasicUnitName = "Tab", PlannedBasicQuantity = 8 },
                            new() { ProductName = "Vitamin C 1000mg", BasicUnitName = "Tab", PackagingUnitName = "Bottle", PlannedBasicQuantity = 0, PlannedPackagingQuantity = 12 }
                        ],
                        Returns =
                        [
                            new() { ProductName = "Paracetamol 500mg", BasicUnitName = "Tab", PackagingUnitName = "Box", BasicUnitPrice = 2.50m, PackagingUnitPrice = 60.00m, BasicQtyReturned = 5, RefundAmount = 12.50m, RefundMethod = "Cash", Reason = "Damaged packaging" },
                            new() { ProductName = "Amoxicillin 250mg", BasicUnitName = "Cap", BasicUnitPrice = 1.80m, BasicQtyReturned = 10, RefundAmount = 18.00m, RefundMethod = "MobileMoney", Reason = "Near expiry date" }
                        ]
                    },
                    new()
                    {
                        Sequence = 3,
                        CustomerName = "Ashaiman Pharma Plus",
                        CustomerCode = "GAR-00003",
                        PrimaryPhoneNumber = "0277 345 678",
                        DistrictName = "Ashaiman",
                        PrimaryLocationLandmark = "Ground floor of the green plaza, beside mobile money booth",
                        PrimaryLocationStreet = "Ashaiman Market Road",
                        Products =
                        [
                            new() { ProductName = "Chloroquine Tablets", BasicUnitName = "Tab", PackagingUnitName = "Pack", PlannedBasicQuantity = 30, PlannedPackagingQuantity = 3 },
                            new() { ProductName = "Ibuprofen 400mg", BasicUnitName = "Tab", PlannedBasicQuantity = 15 },
                            new() { ProductName = "Antacid Suspension", BasicUnitName = "Bottle", PlannedBasicQuantity = 6 },
                            new() { ProductName = "Zinc Sulphate", BasicUnitName = "Tab", PackagingUnitName = "Box", PlannedBasicQuantity = 0, PlannedPackagingQuantity = 10 }
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
