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
                        PrimaryPhoneNumber = "0244 123 456",
                        DistrictName = "Tema",
                        RegionName = "Greater Accra Region",
                        PrimaryLocationLandmark = "Opposite the blue mosque, after the junction",
                        PrimaryLocationStreet = "Community 5, Tema",
                        PrimaryContactName = "Ama Boateng",
                        PrimaryContactPhone = "0209 876 543",
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
                        RegionName = "Greater Accra Region",
                        PrimaryLocationLandmark = "Near the Katamanso police station, red building",
                        PrimaryLocationStreet = "Main Road, Katamanso",
                        PrimaryContactName = "Kofi Mensah",
                        PrimaryContactPhone = "0244 567 890",
                        Products =
                        [
                            new() { ProductName = "Metronidazole 400mg", BasicUnitName = "Tab", PlannedBasicQuantity = 8 },
                            new() { ProductName = "Vitamin C 1000mg", BasicUnitName = "Tab", PackagingUnitName = "Bottle", PlannedBasicQuantity = 0, PlannedPackagingQuantity = 12 }
                        ]
                    },
                    new()
                    {
                        Sequence = 3,
                        CustomerName = "Ashaiman Pharma Plus",
                        CustomerCode = "GAR-00003",
                        PrimaryPhoneNumber = "0277 345 678",
                        DistrictName = "Ashaiman",
                        RegionName = "Greater Accra Region",
                        PrimaryLocationLandmark = "Ground floor of the green plaza, beside mobile money booth",
                        PrimaryLocationStreet = "Ashaiman Market Road",
                        PrimaryContactName = "Abena Asante",
                        PrimaryContactPhone = "0277 111 222",
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
