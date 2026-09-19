using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Customers.CreateCustomer;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class CreateCustomerByDriverToken
{
    public class Command : IRequest<Result<CustomerResponse>>
    {
        public Guid Token { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public CustomerType CustomerType { get; set; }
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string? WhatsAppNumber { get; set; }
        public Guid? ClientGeneratedId { get; set; }

        public RepresentativeInput Representative { get; set; } = new();
        public GpsInput? Gps { get; set; }

        public class RepresentativeInput
        {
            public string FirstName { get; set; } = string.Empty;
            public string? MiddleName { get; set; }
            public string LastName { get; set; } = string.Empty;
            public RelationshipType RelationshipType { get; set; }
            public string PrimaryPhoneNumber { get; set; } = string.Empty;
            public string? GhanaCardNumber { get; set; }
        }

        public class GpsInput
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
        }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.TradingName).MaximumLength(200).When(x => x.TradingName is not null);
            RuleFor(x => x.WhatsAppNumber).MaximumLength(30).When(x => x.WhatsAppNumber is not null);
            RuleFor(x => x.Representative).NotNull();
            RuleFor(x => x.Representative.FirstName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Representative.LastName).NotEmpty().MaximumLength(80);
            RuleFor(x => x.Representative.MiddleName).MaximumLength(80).When(x => x.Representative.MiddleName is not null);
            RuleFor(x => x.Representative.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Gps!.Latitude).InclusiveBetween(-90, 90).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.Longitude).InclusiveBetween(-180, 180).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.AccuracyMetres).GreaterThanOrEqualTo(0).When(x => x.Gps is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<CustomerResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<CustomerResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<CustomerResponse>(Error.ValidationError(validation));

            if (request.ClientGeneratedId.HasValue)
            {
                var duplicate = await _db.CustomerAccounts
                    .FirstOrDefaultAsync(c => c.ClientGeneratedId == request.ClientGeneratedId, cancellationToken);
                if (duplicate is not null)
                    return Result.Success(BuildResponse(duplicate));
            }

            var phoneExists = await _db.CustomerAccounts
                .AnyAsync(c => c.PrimaryPhoneNumber == request.PrimaryPhoneNumber.Trim(), cancellationToken);
            if (phoneExists)
                return Result.Failure<CustomerResponse>(Error.Conflict("A customer with this phone number already exists."));

            var trip = await _db.TrekkingTrips
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Region)
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<CustomerResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;
            var attributedStaff = trip.SalesStaff ?? trip.Driver;
            var owningBranchId = attributedStaff?.BranchId ?? trip.Driver.BranchId;

            var region = trip.Region;

            var count = await _db.CustomerAccounts.CountAsync(c => c.RegionId == region.Id, cancellationToken);
            var customerCode = $"{region.Code.ToUpper()}-{(count + 1):D5}";

            var account = new CustomerAccount
            {
                CustomerCode = customerCode,
                BusinessName = request.BusinessName.Trim(),
                TradingName = request.TradingName?.Trim(),
                CustomerType = request.CustomerType,
                RegionId = region.Id,
                PrimaryPhoneNumber = request.PrimaryPhoneNumber.Trim(),
                WhatsAppNumber = request.WhatsAppNumber?.Trim(),
                OwningBranchId = owningBranchId,
                RegistrationStatus = RegistrationStatus.Active,
                RegisteredByStaffId = attributedStaffId,
                RegisteredDuringTrekId = trip.Id,
                ClientGeneratedId = request.ClientGeneratedId,
                CreatedOffline = true,
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

            _db.CustomerAccounts.Add(account);
            _db.CustomerPersons.Add(person);

            CustomerLocation? location = null;
            if (request.Gps is not null)
            {
                location = new CustomerLocation
                {
                    CustomerAccountId = account.Id,
                    LocationType = LocationType.BusinessPremises,
                    RegionId = region.Id,
                    DistrictId = null,
                    Latitude = request.Gps.Latitude,
                    Longitude = request.Gps.Longitude,
                    AccuracyMetres = request.Gps.AccuracyMetres,
                    CaptureMethod = CaptureMethod.PwaGps,
                    VerificationStatus = LocationVerificationStatus.GpsCaptured,
                    IsPrimary = true,
                    CapturedByStaffId = attributedStaffId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CustomerLocations.Add(location);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new CustomerResponse
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
                OwningBranchName = attributedStaff?.Branch?.Name ?? string.Empty,
                RegisteredByStaffId = account.RegisteredByStaffId,
                RegisteredByName = attributedStaff?.FullName ?? string.Empty,
                RegisteredDuringTrekId = account.RegisteredDuringTrekId,
                ClientGeneratedId = account.ClientGeneratedId,
                CreatedOffline = account.CreatedOffline,
                RecordedAt = account.RecordedAt,
                CreatedAt = account.CreatedAt,
                PrimaryPerson = new CustomerPersonResponse
                {
                    Id = person.Id,
                    FullName = person.FullName,
                    RelationshipType = person.RelationshipType.ToString(),
                    PrimaryPhoneNumber = person.PrimaryPhoneNumber,
                    IsPrimaryContact = person.IsPrimaryContact,
                    IsCreditResponsiblePerson = person.IsCreditResponsiblePerson
                },
                PrimaryLocation = location is null ? null : new CustomerLocationResponse
                {
                    Id = location.Id,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    AccuracyMetres = location.AccuracyMetres,
                    CaptureMethod = location.CaptureMethod.ToString(),
                    VerificationStatus = location.VerificationStatus.ToString(),
                    IsPrimary = location.IsPrimary
                }
            });
        }

        private static CustomerResponse BuildResponse(CustomerAccount account) => new()
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
            ClientGeneratedId = account.ClientGeneratedId,
            CreatedOffline = account.CreatedOffline,
            RecordedAt = account.RecordedAt,
            CreatedAt = account.CreatedAt
        };
    }
}

public class CreateCustomerByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/customers",
            async (Guid token, CreateCustomerByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/customers/{result.Value.Id}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Register a new customer from the field (driver portal)")
        .WithDescription("No authentication required — the driver token acts as the access key. Idempotent: if clientGeneratedId matches an existing record the existing customer is returned.")
        .Produces<Customers.CreateCustomer.CustomerResponse>(201)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
