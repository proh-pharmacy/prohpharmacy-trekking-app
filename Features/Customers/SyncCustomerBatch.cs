using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class SyncCustomerBatch
{
    public class Command : IRequest<Result<SyncResponse>>
    {
        public List<CustomerSyncItem> Items { get; set; } = [];
    }

    public class CustomerSyncItem
    {
        public Guid ClientGeneratedId { get; set; }
        public DateTime RecordedAt { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public CustomerType CustomerType { get; set; }
        public Guid RegionId { get; set; }
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string? WhatsAppNumber { get; set; }
        public Guid? RegisteredDuringTrekId { get; set; }
        public RepresentativeDto Representative { get; set; } = new();
        public LocationDto Location { get; set; } = new();

        public class RepresentativeDto
        {
            public string FirstName { get; set; } = string.Empty;
            public string? MiddleName { get; set; }
            public string LastName { get; set; } = string.Empty;
            public RelationshipType RelationshipType { get; set; }
            public string PrimaryPhoneNumber { get; set; } = string.Empty;
            public string? GhanaCardNumber { get; set; }
        }

        public class LocationDto
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
            public string LandmarkAndDirections { get; set; } = string.Empty;
            public string StreetAddress { get; set; } = string.Empty;
            public Guid DistrictId { get; set; }
        }
    }

    public class SyncResponse
    {
        public int Synced { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<SyncItemResult> Results { get; set; } = [];
    }

    public class SyncItemResult
    {
        public Guid ClientGeneratedId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? Error { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SyncResponse>>
    {
        private readonly AppDbContext _db;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, AuthProvider auth)
        {
            _db = db;
            _auth = auth;
        }

        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.Items is null || request.Items.Count == 0)
                return Result.Failure<SyncResponse>(Error.BadRequest("Batch must contain at least one item."));

            if (!Guid.TryParse(_auth.GetUserId(), out var userId))
                return Result.Failure<SyncResponse>(Error.BadRequest("Invalid user context."));

            var registeredBy = await _db.StaffMembers
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.ApplicationUser!.Id == userId, cancellationToken);

            if (registeredBy is null)
                return Result.Failure<SyncResponse>(Error.CreateNotFoundError("Staff member not found for the current user."));

            var incomingIds = request.Items.Select(i => i.ClientGeneratedId).ToList();
            var existingIds = await _db.CustomerAccounts
                .Where(a => a.ClientGeneratedId != null && incomingIds.Contains(a.ClientGeneratedId!.Value))
                .Select(a => new { a.ClientGeneratedId, a.Id, a.CustomerCode })
                .ToListAsync(cancellationToken);

            var existingMap = existingIds.ToDictionary(a => a.ClientGeneratedId!.Value);

            var response = new SyncResponse();

            foreach (var item in request.Items)
            {
                if (existingMap.TryGetValue(item.ClientGeneratedId, out var existing))
                {
                    response.Results.Add(new SyncItemResult
                    {
                        ClientGeneratedId = item.ClientGeneratedId,
                        Status = "AlreadySynced",
                        CustomerId = existing.Id,
                        CustomerCode = existing.CustomerCode
                    });
                    response.Skipped++;
                    continue;
                }

                var itemResult = await ProcessItemAsync(item, registeredBy.Id, registeredBy.BranchId, cancellationToken);
                response.Results.Add(new SyncItemResult
                {
                    ClientGeneratedId = item.ClientGeneratedId,
                    Status = itemResult.IsSuccess ? "Created" : "Failed",
                    CustomerId = itemResult.IsSuccess ? itemResult.Value.Id : null,
                    CustomerCode = itemResult.IsSuccess ? itemResult.Value.CustomerCode : null,
                    Error = itemResult.IsFailure ? itemResult.Error.Message : null
                });

                if (itemResult.IsSuccess) response.Synced++;
                else response.Failed++;
            }

            return Result.Success(response);
        }

        private async Task<Result<CustomerAccount>> ProcessItemAsync(
            CustomerSyncItem item, Guid staffId, Guid branchId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(item.BusinessName))
                return Result.Failure<CustomerAccount>(Error.BadRequest("BusinessName is required."));
            if (string.IsNullOrWhiteSpace(item.PrimaryPhoneNumber))
                return Result.Failure<CustomerAccount>(Error.BadRequest("PrimaryPhoneNumber is required."));
            if (string.IsNullOrWhiteSpace(item.Representative.FirstName) || string.IsNullOrWhiteSpace(item.Representative.LastName))
                return Result.Failure<CustomerAccount>(Error.BadRequest("Representative first and last name are required."));
            if (string.IsNullOrWhiteSpace(item.Representative.PrimaryPhoneNumber))
                return Result.Failure<CustomerAccount>(Error.BadRequest("Representative phone number is required."));
            if (string.IsNullOrWhiteSpace(item.Location.LandmarkAndDirections))
                return Result.Failure<CustomerAccount>(Error.BadRequest("LandmarkAndDirections is required."));
            if (string.IsNullOrWhiteSpace(item.Location.StreetAddress))
                return Result.Failure<CustomerAccount>(Error.BadRequest("StreetAddress is required."));

            var region = await _db.Regions.FirstOrDefaultAsync(r => r.Id == item.RegionId, ct);
            if (region is null)
                return Result.Failure<CustomerAccount>(Error.BadRequest("Region not found."));

            var district = await _db.Districts.FirstOrDefaultAsync(d => d.Id == item.Location.DistrictId, ct);
            if (district is null)
                return Result.Failure<CustomerAccount>(Error.BadRequest("District not found."));

            var count = await _db.CustomerAccounts.CountAsync(c => c.RegionId == item.RegionId, ct);
            var customerCode = $"{region.Code.ToUpper()}-{(count + 1):D5}";

            var account = new CustomerAccount
            {
                CustomerCode = customerCode,
                BusinessName = item.BusinessName.Trim(),
                TradingName = item.TradingName?.Trim(),
                CustomerType = item.CustomerType,
                RegionId = item.RegionId,
                PrimaryPhoneNumber = item.PrimaryPhoneNumber.Trim(),
                WhatsAppNumber = item.WhatsAppNumber?.Trim(),
                OwningBranchId = branchId,
                RegistrationStatus = RegistrationStatus.Active,
                RegisteredByStaffId = staffId,
                RegisteredDuringTrekId = item.RegisteredDuringTrekId,
                ClientGeneratedId = item.ClientGeneratedId,
                CreatedOffline = true,
                RecordedAt = item.RecordedAt,
                CreatedAt = DateTime.UtcNow
            };

            var person = new CustomerPerson
            {
                CustomerAccountId = account.Id,
                FirstName = item.Representative.FirstName.Trim(),
                MiddleName = item.Representative.MiddleName?.Trim(),
                LastName = item.Representative.LastName.Trim(),
                RelationshipType = item.Representative.RelationshipType,
                PrimaryPhoneNumber = item.Representative.PrimaryPhoneNumber.Trim(),
                GhanaCardNumber = item.Representative.GhanaCardNumber?.Trim(),
                IsPrimaryContact = true,
                IsCreditResponsiblePerson = true,
                CreatedAt = DateTime.UtcNow
            };

            var captureMethod = item.Location.AccuracyMetres > 0
                ? CaptureMethod.PwaGps
                : CaptureMethod.ManualLocationSelection;

            var location = new CustomerLocation
            {
                CustomerAccountId = account.Id,
                LocationType = LocationType.BusinessPremises,
                RegionId = item.RegionId,
                DistrictId = item.Location.DistrictId,
                StreetAddress = item.Location.StreetAddress.Trim(),
                LandmarkAndDirections = item.Location.LandmarkAndDirections.Trim(),
                Latitude = item.Location.Latitude,
                Longitude = item.Location.Longitude,
                AccuracyMetres = item.Location.AccuracyMetres,
                CaptureMethod = captureMethod,
                VerificationStatus = captureMethod == CaptureMethod.PwaGps
                    ? LocationVerificationStatus.GpsCaptured
                    : LocationVerificationStatus.Unverified,
                IsPrimary = true,
                CapturedByStaffId = staffId,
                CreatedAt = DateTime.UtcNow
            };

            _db.CustomerAccounts.Add(account);
            _db.CustomerPersons.Add(person);
            _db.CustomerLocations.Add(location);
            await _db.SaveChangesAsync(ct);

            return Result.Success(account);
        }
    }
}

public class SyncCustomerBatchEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/sync", async (SyncCustomerBatch.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Batch sync offline-created customers")
        .WithDescription(
            "Accepts a batch of customers created offline on the PWA. Each item is processed independently — " +
            "the batch never fails as a whole. Items already synced (matched by `clientGeneratedId`) are skipped and " +
            "returned as `AlreadySynced`. `recordedAt` is the device timestamp; `createdAt` is set by the server on receipt.")
        .Produces<SyncCustomerBatch.SyncResponse>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
