using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Services.Pdf;

namespace prohpharmacy_trekking_app.Services.Jobs;

public class TrekEmailJob(AppDbContext db, IEmailService email, IConfiguration config)
{
    public async Task SendTrekStartEmailsAsync(Guid trekId, Guid driverToken)
    {
        var trip = await db.TrekkingTrips
            .Include(t => t.Driver)
            .Include(t => t.SalesStaff)
            .Include(t => t.Vehicle)
            .Include(t => t.Region)
            .Include(t => t.Stops.OrderBy(s => s.Sequence))
                .ThenInclude(s => s.CustomerAccount)
                    .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                        .ThenInclude(l => l.District)
            .Include(t => t.Stops)
                .ThenInclude(s => s.CustomerAccount)
                    .ThenInclude(ca => ca.Region)
            .Include(t => t.Stops)
                .ThenInclude(s => s.CustomerAccount)
                    .ThenInclude(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
            .Include(t => t.Stops)
                .ThenInclude(s => s.Products)
                    .ThenInclude(p => p.Product)
                        .ThenInclude(p => p.BasicUnit)
            .Include(t => t.Stops)
                .ThenInclude(s => s.Products)
                    .ThenInclude(p => p.Product)
                        .ThenInclude(p => p.PackagingUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trekId);

        if (trip is null) return;

        var frontendUrl = config["SiteSettings:FrontendUrl"]?.TrimEnd('/') ?? string.Empty;
        var appName = config["SiteSettings:AppName"] ?? "Proh Pharmacy";
        var supportEmail = config["EmailSettings:SupportEmail"] ?? string.Empty;
        var driverLinkUrl = $"{frontendUrl}/treks/driver?token={driverToken}";

        var pdfData = new TrekkingSheetPdfGenerator.TrekkingSheetData
        {
            TrekNumber = trip.TrekNumber,
            ScheduledDate = trip.ScheduledDate,
            DriverName = trip.Driver?.FullName ?? string.Empty,
            VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
            RegionName = trip.Region?.Name ?? string.Empty,
            DriverToken = driverToken,
            FrontendUrl = config["SiteSettings:FrontendUrl"],
            Stops = trip.Stops.OrderBy(s => s.Sequence).Select(s =>
            {
                var loc = s.CustomerAccount?.Locations.FirstOrDefault();
                return new TrekkingSheetPdfGenerator.TrekkingSheetData.StopData
                {
                    Sequence = s.Sequence,
                    CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                    CustomerCode = s.CustomerAccount?.CustomerCode ?? string.Empty,
                    PrimaryPhoneNumber = s.CustomerAccount?.PrimaryPhoneNumber,
                    DistrictName = loc?.District?.Name,
                    PrimaryLocationLandmark = loc?.LandmarkAndDirections,
                    PrimaryLocationStreet = loc?.StreetAddress,
                    Products = s.Products.Select(p => new TrekkingSheetPdfGenerator.TrekkingSheetData.ProductData
                    {
                        ProductName = p.Product?.Name ?? string.Empty,
                        BasicUnitName = p.Product?.BasicUnit?.Name,
                        PackagingUnitName = p.Product?.PackagingUnit?.Name,
                        PlannedBasicQuantity = p.PlannedBasicQuantity,
                        PlannedPackagingQuantity = p.PlannedPackagingQuantity,
                        BasicQtyDelivered = p.BasicQtyDelivered,
                        PackagingQtyDelivered = p.PackagingQtyDelivered,
                        PaymentMethod = p.PaymentMethod?.ToString(),
                        AmtPaid = p.AmtPaid,
                        Balance = p.Balance,
                        Notes = p.Notes
                    }).ToList()
                };
            }).ToList()
        };
        var pdfBytes = TrekkingSheetPdfGenerator.Generate(pdfData);

        var recipients = new List<(string FullName, string Email)>
        {
            (trip.Driver.FullName, trip.Driver.EmailAddress)
        };
        if (trip.SalesStaff is not null &&
            !trip.Driver.EmailAddress.Equals(trip.SalesStaff.EmailAddress, StringComparison.OrdinalIgnoreCase))
            recipients.Add((trip.SalesStaff.FullName, trip.SalesStaff.EmailAddress));

        foreach (var (fullName, emailAddr) in recipients)
        {
            var model = new TrekAssignmentEmailModel
            {
                RecipientName = fullName,
                TrekNumber = trip.TrekNumber,
                ScheduledDate = trip.ScheduledDate.ToString("dd MMM yyyy"),
                DriverName = trip.Driver?.FullName ?? string.Empty,
                BranchName = trip.Region?.Name ?? string.Empty,
                DriverLinkUrl = driverLinkUrl,
                AppName = appName,
                SupportEmail = supportEmail
            };
            await email.SendTrekAssignmentEmailAsync(emailAddr, model, pdfBytes);
        }
    }
}
