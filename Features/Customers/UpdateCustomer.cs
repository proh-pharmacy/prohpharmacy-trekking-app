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
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public CustomerType CustomerType { get; set; }
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.PrimaryPhoneNumber).NotEmpty().MaximumLength(30);
            RuleFor(x => x.TradingName).MaximumLength(200).When(x => x.TradingName is not null);
            RuleFor(x => x.WhatsAppNumber).MaximumLength(30).When(x => x.WhatsAppNumber is not null);
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

            account.BusinessName = request.BusinessName.Trim();
            account.TradingName = request.TradingName?.Trim();
            account.CustomerType = request.CustomerType;
            account.PrimaryPhoneNumber = request.PrimaryPhoneNumber.Trim();
            account.WhatsAppNumber = request.WhatsAppNumber?.Trim();
            account.UpdatedAt = DateTime.UtcNow;

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
        .WithSummary("Update customer business details")
        .Produces<CreateCustomer.CustomerResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
