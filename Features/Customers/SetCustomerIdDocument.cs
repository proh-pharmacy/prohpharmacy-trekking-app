using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class SetCustomerIdDocument
{
    public class Command : IRequest<Result<IdDocumentResponse>>
    {
        public Guid CustomerId { get; set; }
        public CustomerIdDocumentType IdDocumentType { get; set; }
        public string IdDocumentNumber { get; set; } = string.Empty;
    }

    public class IdDocumentResponse
    {
        public Guid CustomerId { get; set; }
        public string IdDocumentType { get; set; } = string.Empty;
        public string IdDocumentNumber { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.IdDocumentNumber)
                .NotEmpty().WithMessage("Document number is required.")
                .MaximumLength(100).WithMessage("Document number must be 100 characters or fewer.");
        }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<IdDocumentResponse>>
    {
        public async Task<Result<IdDocumentResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var account = await db.CustomerAccounts
                .FirstOrDefaultAsync(a => a.Id == request.CustomerId, cancellationToken);

            if (account is null)
                return Result.Failure<IdDocumentResponse>(Error.CreateNotFoundError("Customer not found."));

            var duplicate = await db.CustomerAccounts
                .AnyAsync(a => a.IdDocumentNumber == request.IdDocumentNumber && a.Id != request.CustomerId, cancellationToken);

            if (duplicate)
                return Result.Failure<IdDocumentResponse>(Error.Conflict("A customer with this document number already exists."));

            account.IdDocumentType = request.IdDocumentType;
            account.IdDocumentNumber = request.IdDocumentNumber;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new IdDocumentResponse
            {
                CustomerId = account.Id,
                IdDocumentType = account.IdDocumentType.ToString()!,
                IdDocumentNumber = account.IdDocumentNumber
            });
        }
    }
}

public class SetCustomerIdDocumentEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/{customerId:guid}/id-document",
            async (Guid customerId, SetCustomerIdDocument.Command command, ISender sender) =>
            {
                command.CustomerId = customerId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Set the ID document type and number for a customer")
        .Produces<SetCustomerIdDocument.IdDocumentResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
