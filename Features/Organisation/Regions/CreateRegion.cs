using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Organisation.Regions;

public static class CreateRegion
{
    public class Command : IRequest<Result<RegionResponse>>
    {
        public string Name { get; set; } = string.Empty;
    }

    public class RegionResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<RegionResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<RegionResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<RegionResponse>(Error.ValidationError(validation));

            var nameExists = await _db.Regions
                .AnyAsync(r => r.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (nameExists)
                return Result.Failure<RegionResponse>(Error.Conflict("A region with this name already exists."));

            var code = await StringUtilities.GenerateUniqueCodeAsync(
                request.Name,
                c => _db.Regions.AnyAsync(r => r.Code == c, cancellationToken));

            var region = new Region
            {
                Code = code,
                Name = request.Name.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Regions.Add(region);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(region));
        }

        internal static RegionResponse ToResponse(Region r) => new()
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt
        };
    }
}

public class CreateRegionEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/organisation/regions", async (CreateRegion.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/organisation/regions/{result.Value.Id}", result.Value);
        })
        .WithTags("Organisation - Regions")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Create a new region")
        .RequireAuthorization();
    }
}
