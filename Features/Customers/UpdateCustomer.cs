using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class UpdateCustomer
{
    public class Command : IRequest<Result<CreateCustomer.CustomerResponse>>
    {
        public Guid Id { get; set; }

        // Business
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public CustomerType CustomerType { get; set; }
        public Guid RegionId { get; set; }
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }

        // Representative
        public RepresentativeDto? Representative { get; set; }

        // Primary location
        public LocationDto? Location { get; set; }

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
            public Guid DistrictId { get; set; }
            public string? StreetAddress { get; set; }
            public string? LandmarkAndDirections { get; set; }
            public decimal? Latitude { get; set; }
            public decimal? Longitude { get; set; }
            public decimal? AccuracyMetres { get; set; }
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
            RuleFor(x => x.RegionId).NotEmpty();

            When(x => x.Representative is not null, () =>
            {
                RuleFor(x => x.Representative!.FirstName).NotEmpty().MaximumLength(80);
                RuleFor(x => x.Representative!.LastName).NotEmpty().MaximumLength(80);
                RuleFor(x => x.Representative!.MiddleName).MaximumLength(80).When(x => x.Representative!.MiddleName is not null);
                RuleFor(x => x.Representative!.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
                RuleFor(x => x.Representative!.GhanaCardNumber).MaximumLength(30).When(x => x.Representative!.GhanaCardNumber is not null);
            });

            When(x => x.Location is not null, () =>
            {
                RuleFor(x => x.Location!.DistrictId).NotEmpty();
                RuleFor(x => x.Location!.StreetAddress).MaximumLength(300).When(x => x.Location!.StreetAddress is not null);
                RuleFor(x => x.Location!.LandmarkAndDirections).MaximumLength(500).When(x => x.Location!.LandmarkAndDirections is not null);
                RuleFor(x => x.Location!.Latitude).InclusiveBetween(-90, 90).When(x => x.Location!.Latitude.HasValue);
                RuleFor(x => x.Location!.Longitude).InclusiveBetween(-180, 180).When(x => x.Location!.Longitude.HasValue);
                RuleFor(x => x.Location!.AccuracyMetres).GreaterThan(0).When(x => x.Location!.AccuracyMetres.HasValue);
            });
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<CreateCustomer.CustomerResponse>>
    {
        public async Task<Result<CreateCustomer.CustomerResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<CreateCustomer.CustomerResponse>(Error.ValidationError(validation));

            var account = await db.CustomerAccounts
                .Include(a => a.Region)
                .Include(a => a.OwningBranch)
                .Include(a => a.RegisteredBy)
                .Include(a => a.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(a => a.Locations.Where(l => l.IsPrimary))
                    .ThenInclude(l => l.District)
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            if (account is null)
                return Result.Failure<CreateCustomer.CustomerResponse>(Error.CreateNotFoundError("Customer not found."));

            // If region changed, re-validate it and update the customer code
            if (account.RegionId != request.RegionId)
            {
                var newRegion = await db.Regions.FindAsync([request.RegionId], cancellationToken);
                if (newRegion is null)
                    return Result.Failure<CreateCustomer.CustomerResponse>(Error.CreateNotFoundError("Region not found."));

                var count = await db.CustomerAccounts.CountAsync(c => c.RegionId == request.RegionId, cancellationToken);
                account.CustomerCode = $"{newRegion.Code.ToUpper()}-{(count + 1):D5}";
                account.RegionId = request.RegionId;
            }

            account.BusinessName = request.BusinessName.Trim();
            account.TradingName = request.TradingName?.Trim();
            account.CustomerType = request.CustomerType;
            account.PrimaryPhoneNumber = request.PrimaryPhoneNumber.Trim();
            account.WhatsAppNumber = request.WhatsAppNumber?.Trim();
            account.UpdatedAt = DateTime.UtcNow;

            // Update primary representative if provided
            if (request.Representative is not null)
            {
                var person = account.People.FirstOrDefault();
                if (person is not null)
                {
                    person.FirstName = request.Representative.FirstName.Trim();
                    person.MiddleName = request.Representative.MiddleName?.Trim();
                    person.LastName = request.Representative.LastName.Trim();
                    person.RelationshipType = request.Representative.RelationshipType;
                    person.PrimaryPhoneNumber = request.Representative.PrimaryPhoneNumber.Trim();
                    person.GhanaCardNumber = request.Representative.GhanaCardNumber?.Trim();
                    person.UpdatedAt = DateTime.UtcNow;
                }
            }

            // Update primary location if provided
            if (request.Location is not null)
            {
                var location = account.Locations.FirstOrDefault();
                if (location is not null)
                {
                    if (request.Location.DistrictId != Guid.Empty)
                    {
                        var district = await db.Districts.FindAsync([request.Location.DistrictId], cancellationToken);
                        if (district is null)
                            return Result.Failure<CreateCustomer.CustomerResponse>(Error.CreateNotFoundError("District not found."));
                        location.DistrictId = request.Location.DistrictId;
                        location.RegionId = request.RegionId;
                    }
                    location.StreetAddress = request.Location.StreetAddress?.Trim();
                    location.LandmarkAndDirections = request.Location.LandmarkAndDirections?.Trim();
                    if (request.Location.Latitude.HasValue) location.Latitude = request.Location.Latitude;
                    if (request.Location.Longitude.HasValue) location.Longitude = request.Location.Longitude;
                    if (request.Location.AccuracyMetres.HasValue) location.AccuracyMetres = request.Location.AccuracyMetres;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var primaryPerson = account.People.FirstOrDefault();
            var primaryLocation = account.Locations.FirstOrDefault();

            return Result.Success(CreateCustomer.Handler.ToResponse(
                account,
                account.Region,
                account.OwningBranch,
                account.RegisteredBy,
                primaryPerson,
                primaryLocation,
                primaryLocation?.District));
        }
    }
}

public class UpdateCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/customers/{id:guid}", async (Guid id, UpdateCustomer.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Update customer details")
        .WithDescription("Updates business fields, primary representative, and primary location in a single request. Omit `representative` or `location` to leave them unchanged.")
        .Produces<CreateCustomer.CustomerResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
