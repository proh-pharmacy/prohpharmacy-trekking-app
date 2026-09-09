using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class CreateCustomer
{
    public class Command : IRequest<Result<CustomerResponse>>
    {
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
            public string? LandmarkAndDirections { get; set; }
            public string? StreetAddress { get; set; }
            public Guid DistrictId { get; set; }
        }
    }

    public class CustomerResponse
    {
        public Guid Id { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string CustomerType { get; set; } = string.Empty;
        public string RegistrationStatus { get; set; } = string.Empty;
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public Guid OwningBranchId { get; set; }
        public string OwningBranchName { get; set; } = string.Empty;
        public Guid RegisteredByStaffId { get; set; }
        public string RegisteredByName { get; set; } = string.Empty;
        public Guid? RegisteredDuringTrekId { get; set; }
        public Guid? ClientGeneratedId { get; set; }
        public bool CreatedOffline { get; set; }
        public DateTime RecordedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public CustomerPersonResponse? PrimaryPerson { get; set; }
        public CustomerLocationResponse? PrimaryLocation { get; set; }
    }

    public class CustomerPersonResponse
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string RelationshipType { get; set; } = string.Empty;
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public bool IsPrimaryContact { get; set; }
        public bool IsCreditResponsiblePerson { get; set; }
        public string? PortraitUrl { get; set; }
    }

    public class CustomerLocationResponse
    {
        public Guid Id { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public decimal? AccuracyMetres { get; set; }
        public string LandmarkAndDirections { get; set; } = string.Empty;
        public string? StreetAddress { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string DistrictName { get; set; } = string.Empty;
        public string CaptureMethod { get; set; } = string.Empty;
        public string VerificationStatus { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.TradingName).MaximumLength(200).When(x => x.TradingName is not null);
            RuleFor(x => x.WhatsAppNumber).MaximumLength(30).When(x => x.WhatsAppNumber is not null);

            RuleFor(x => x.Representative).NotNull();
            RuleFor(x => x.Representative.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Representative.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Representative.MiddleName).MaximumLength(80).When(x => x.Representative.MiddleName is not null);
            RuleFor(x => x.Representative.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Representative.GhanaCardNumber).MaximumLength(30).When(x => x.Representative.GhanaCardNumber is not null);

            RuleFor(x => x.Location).NotNull();
            RuleFor(x => x.Location.DistrictId).NotEmpty();
            RuleFor(x => x.Location.LandmarkAndDirections).MaximumLength(500).When(x => x.Location.LandmarkAndDirections is not null);
            RuleFor(x => x.Location.StreetAddress).MaximumLength(300).When(x => x.Location.StreetAddress is not null);
            RuleFor(x => x.Location.Latitude).InclusiveBetween(-90, 90);
            RuleFor(x => x.Location.Longitude).InclusiveBetween(-180, 180);
            RuleFor(x => x.Location.AccuracyMetres).GreaterThan(0);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<CustomerResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        {
            _db = db;
            _validator = validator;
            _auth = auth;
        }

        public async Task<Result<CustomerResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<CustomerResponse>(Error.ValidationError(validation));

            if (!Guid.TryParse(_auth.GetUserId(), out var userId))
                return Result.Failure<CustomerResponse>(Error.BadRequest("Invalid user context."));

            var registeredBy = await _db.StaffMembers
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.ApplicationUser!.Id == userId, cancellationToken);

            if (registeredBy is null)
                return Result.Failure<CustomerResponse>(Error.CreateNotFoundError("Staff member not found for the current user."));

            var region = await _db.Regions.FirstOrDefaultAsync(r => r.Id == request.RegionId, cancellationToken);
            if (region is null)
                return Result.Failure<CustomerResponse>(Error.CreateNotFoundError("Region not found."));

            var district = await _db.Districts.FirstOrDefaultAsync(d => d.Id == request.Location.DistrictId, cancellationToken);
            if (district is null)
                return Result.Failure<CustomerResponse>(Error.CreateNotFoundError("District not found."));

            var count = await _db.CustomerAccounts.CountAsync(c => c.RegionId == request.RegionId, cancellationToken);
            var customerCode = $"{region.Code.ToUpper()}-{(count + 1):D5}";

            var account = new CustomerAccount
            {
                CustomerCode = customerCode,
                BusinessName = request.BusinessName.Trim(),
                TradingName = request.TradingName?.Trim(),
                CustomerType = request.CustomerType,
                RegionId = request.RegionId,
                PrimaryPhoneNumber = request.PrimaryPhoneNumber.Trim(),
                WhatsAppNumber = request.WhatsAppNumber?.Trim(),
                OwningBranchId = registeredBy.BranchId,
                RegistrationStatus = RegistrationStatus.Active,
                RegisteredByStaffId = registeredBy.Id,
                RegisteredDuringTrekId = request.RegisteredDuringTrekId,
                CreatedOffline = false,
                RecordedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var person = new CustomerPerson
            {
                CustomerAccountId = account.Id,
                FirstName = request.Representative.FirstName.Trim(),
                MiddleName = request.Representative.MiddleName?.Trim(),
                LastName = request.Representative.LastName.Trim(),
                RelationshipType = request.Representative.RelationshipType,
                PrimaryPhoneNumber = request.Representative.PrimaryPhoneNumber.Trim(),
                GhanaCardNumber = request.Representative.GhanaCardNumber?.Trim(),
                IsPrimaryContact = true,
                IsCreditResponsiblePerson = true,
                CreatedAt = DateTime.UtcNow
            };

            var captureMethod = request.Location.AccuracyMetres > 0
                ? CaptureMethod.PwaGps
                : CaptureMethod.ManualLocationSelection;

            var location = new CustomerLocation
            {
                CustomerAccountId = account.Id,
                LocationType = LocationType.BusinessPremises,
                RegionId = request.RegionId,
                DistrictId = request.Location.DistrictId,
                StreetAddress = request.Location.StreetAddress?.Trim(),
                LandmarkAndDirections = request.Location.LandmarkAndDirections?.Trim(),
                Latitude = request.Location.Latitude,
                Longitude = request.Location.Longitude,
                AccuracyMetres = request.Location.AccuracyMetres,
                CaptureMethod = captureMethod,
                VerificationStatus = captureMethod == CaptureMethod.PwaGps
                    ? LocationVerificationStatus.GpsCaptured
                    : LocationVerificationStatus.Unverified,
                IsPrimary = true,
                CapturedByStaffId = registeredBy.Id,
                CreatedAt = DateTime.UtcNow
            };

            _db.CustomerAccounts.Add(account);
            _db.CustomerPersons.Add(person);
            _db.CustomerLocations.Add(location);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(account, region, registeredBy.Branch, registeredBy, person, location, district));
        }

        internal static CustomerResponse ToResponse(
            CustomerAccount account,
            Region region,
            Branch branch,
            StaffMember registeredBy,
            CustomerPerson? primaryPerson,
            CustomerLocation? primaryLocation,
            District? district) => new()
        {
            Id = account.Id,
            CustomerCode = account.CustomerCode,
            BusinessName = account.BusinessName,
            TradingName = account.TradingName,
            CustomerType = account.CustomerType.ToString(),
            RegistrationStatus = account.RegistrationStatus.ToString(),
            PrimaryPhoneNumber = account.PrimaryPhoneNumber,
            WhatsAppNumber = account.WhatsAppNumber,
            RegionId = account.RegionId,
            RegionName = region.Name,
            OwningBranchId = account.OwningBranchId,
            OwningBranchName = branch.Name,
            RegisteredByStaffId = account.RegisteredByStaffId,
            RegisteredByName = registeredBy.FullName,
            RegisteredDuringTrekId = account.RegisteredDuringTrekId,
            ClientGeneratedId = account.ClientGeneratedId,
            CreatedOffline = account.CreatedOffline,
            RecordedAt = account.RecordedAt,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            PrimaryPerson = primaryPerson is null ? null : new CustomerPersonResponse
            {
                Id = primaryPerson.Id,
                FullName = primaryPerson.FullName,
                RelationshipType = primaryPerson.RelationshipType.ToString(),
                PrimaryPhoneNumber = primaryPerson.PrimaryPhoneNumber,
                IsPrimaryContact = primaryPerson.IsPrimaryContact,
                IsCreditResponsiblePerson = primaryPerson.IsCreditResponsiblePerson,
                PortraitUrl = primaryPerson.PortraitUrl
            },
            PrimaryLocation = primaryLocation is null ? null : new CustomerLocationResponse
            {
                Id = primaryLocation.Id,
                Latitude = primaryLocation.Latitude,
                Longitude = primaryLocation.Longitude,
                AccuracyMetres = primaryLocation.AccuracyMetres,
                LandmarkAndDirections = primaryLocation.LandmarkAndDirections,
                StreetAddress = primaryLocation.StreetAddress,
                RegionName = region.Name,
                DistrictName = district?.Name ?? string.Empty,
                CaptureMethod = primaryLocation.CaptureMethod.ToString(),
                VerificationStatus = primaryLocation.VerificationStatus.ToString(),
                IsPrimary = primaryLocation.IsPrimary
            }
        };
    }
}

public class CreateCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers", async (CreateCustomer.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/customers/{result.Value.Id}", result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Register a new customer")
        .WithDescription(
            "Registers a customer account with a representative and their GPS location in a single request. " +
            "The owning branch is resolved from the authenticated staff member's branch. " +
            "Location verification status is set to `GpsCaptured` when coordinates are provided.")
        .Produces<CreateCustomer.CustomerResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
