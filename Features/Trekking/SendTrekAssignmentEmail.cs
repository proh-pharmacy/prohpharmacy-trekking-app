using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class SendTrekAssignmentEmail
{
    public class Command : IRequest<Result<SendEmailResponse>>
    {
        public Guid TrekId { get; set; }
        public List<Guid> StaffIds { get; set; } = [];
    }

    public class SendEmailResponse
    {
        public string TrekNumber { get; set; } = string.Empty;
        public int Sent { get; set; }
        public List<string> Recipients { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db, IEmailService email, IConfiguration config)
        : IRequestHandler<Command, Result<SendEmailResponse>>
    {
        public async Task<Result<SendEmailResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.StaffIds is null || request.StaffIds.Count == 0)
                return Result.Failure<SendEmailResponse>(Error.BadRequest("At least one staff member must be specified."));

            var trip = await db.TrekkingTrips
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
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
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<SendEmailResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            // Generate driver token if not already set
            if (trip.DriverToken is null)
            {
                trip.DriverToken = Guid.NewGuid();
                trip.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var frontendUrl = config["SiteSettings:FrontendUrl"]?.TrimEnd('/') ?? string.Empty;
            var appName = config["SiteSettings:AppName"] ?? "Proh Pharmacy";
            var supportEmail = config["EmailSettings:SupportEmail"] ?? string.Empty;
            var driverLinkUrl = $"{frontendUrl}/treks/driver?token={trip.DriverToken}";

            // Generate PDF
            var pdfData = new TrekkingSheetPdfGenerator.TrekkingSheetData
            {
                TrekNumber = trip.TrekNumber,
                ScheduledDate = trip.ScheduledDate,
                DriverName = trip.Driver?.FullName ?? string.Empty,
                VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
                BranchName = trip.Branch?.Name ?? string.Empty,
                Stops = trip.Stops.OrderBy(s => s.Sequence).Select(s =>
                {
                    var loc = s.CustomerAccount?.Locations.FirstOrDefault();
                    var contact = s.CustomerAccount?.People.FirstOrDefault();
                    return new TrekkingSheetPdfGenerator.TrekkingSheetData.StopData
                    {
                        Sequence = s.Sequence,
                        CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                        CustomerCode = s.CustomerAccount?.CustomerCode ?? string.Empty,
                        PrimaryPhoneNumber = s.CustomerAccount?.PrimaryPhoneNumber,
                        DistrictName = loc?.District?.Name,
                        RegionName = s.CustomerAccount?.Region?.Name,
                        PrimaryLocationLandmark = loc?.LandmarkAndDirections,
                        PrimaryLocationStreet = loc?.StreetAddress,
                        PrimaryContactName = contact?.FullName,
                        PrimaryContactPhone = contact?.PrimaryPhoneNumber,
                        Products = s.Products.Select(p => new TrekkingSheetPdfGenerator.TrekkingSheetData.ProductData
                        {
                            ProductName = p.Product?.Name ?? string.Empty,
                            Unit = p.Product?.Unit,
                            PlannedQuantity = p.PlannedQuantity,
                            QtyDelivered = p.QtyDelivered,
                            PaymentMethod = p.PaymentMethod?.ToString(),
                            AmtPaid = p.AmtPaid,
                            Balance = p.Balance,
                            Notes = p.Notes
                        }).ToList()
                    };
                }).ToList()
            };
            var pdfBytes = TrekkingSheetPdfGenerator.Generate(pdfData);

            // Load staff members
            var staffMembers = await db.StaffMembers
                .Where(s => request.StaffIds.Contains(s.Id))
                .Select(s => new { s.Id, s.FullName, s.EmailAddress })
                .ToListAsync(cancellationToken);

            var emailModel = new TrekAssignmentEmailModel
            {
                TrekNumber = trip.TrekNumber,
                ScheduledDate = trip.ScheduledDate.ToString("dd MMM yyyy"),
                DriverName = trip.Driver?.FullName ?? string.Empty,
                BranchName = trip.Branch?.Name ?? string.Empty,
                DriverLinkUrl = driverLinkUrl,
                AppName = appName,
                SupportEmail = supportEmail
            };

            var recipients = new List<string>();
            foreach (var staff in staffMembers)
            {
                emailModel.RecipientName = staff.FullName;
                _ = email.SendTrekAssignmentEmailAsync(staff.EmailAddress, emailModel, pdfBytes);
                recipients.Add($"{staff.FullName} <{staff.EmailAddress}>");
            }

            return Result.Success(new SendEmailResponse
            {
                TrekNumber = trip.TrekNumber,
                Sent = staffMembers.Count,
                Recipients = recipients
            });
        }
    }
}

public class SendTrekAssignmentEmailEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{id:guid}/send-email", async (Guid id, SendTrekAssignmentEmail.Command command, ISender sender) =>
        {
            command.TrekId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Send trek assignment email to staff")
        .WithDescription("Sends the trekking sheet PDF and driver form link to one or more staff members by their IDs. Generates the driver token automatically if not yet created.")
        .Produces<SendTrekAssignmentEmail.SendEmailResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
