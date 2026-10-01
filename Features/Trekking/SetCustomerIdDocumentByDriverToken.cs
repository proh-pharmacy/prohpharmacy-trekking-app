using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class SetCustomerIdDocumentByDriverToken
{
    public class Command : IRequest<Result<SetCustomerIdDocument.IdDocumentResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public CustomerIdDocumentType? IdDocumentType { get; set; }
        public string? IdDocumentNumber { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<SetCustomerIdDocument.IdDocumentResponse>>
    {
        public async Task<Result<SetCustomerIdDocument.IdDocumentResponse>> Handle(Command request, CancellationToken ct)
        {
            var trip = await db.TrekkingTrips.AsNoTracking().FirstOrDefaultAsync(t => t.DriverToken == request.Token, ct);
            if (trip is null)
                return Result.Failure<SetCustomerIdDocument.IdDocumentResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));
            var account = await db.CustomerAccounts.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.RegionId == trip.RegionId, ct);
            if (account is null)
                return Result.Failure<SetCustomerIdDocument.IdDocumentResponse>(Error.CreateNotFoundError("Customer not found in this trek's region."));

            var error = CustomerIdDocumentInput.Validate(request.IdDocumentType, request.IdDocumentNumber);
            if (error is not null)
                return Result.Failure<SetCustomerIdDocument.IdDocumentResponse>(new Error("422", error));
            var number = request.IdDocumentNumber!.Trim();
            if (await CustomerIdDocumentInput.IsDuplicateAsync(db, account.Id, number, ct))
                return Result.Failure<SetCustomerIdDocument.IdDocumentResponse>(Error.Conflict("A customer with this document number already exists."));

            account.IdDocumentType = request.IdDocumentType;
            account.IdDocumentNumber = number;
            account.UpdatedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_CustomerAccounts_IdDocumentNumber" })
            {
                return Result.Failure<SetCustomerIdDocument.IdDocumentResponse>(Error.Conflict("A customer with this document number already exists."));
            }
            return Result.Success(new SetCustomerIdDocument.IdDocumentResponse
            {
                CustomerId = account.Id,
                IdDocumentType = account.IdDocumentType!.Value.ToString(),
                IdDocumentNumber = number
            });
        }
    }
}

public class SetCustomerIdDocumentByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/customers/{customerId:guid}/id-document",
            async (Guid token, Guid customerId, SetCustomerIdDocumentByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.CustomerId = customerId;
                var result = await sender.Send(command);
                return result.IsFailure ? Results.UnprocessableEntity(result.Error) : Results.Ok(result.Value);
            })
            .WithTags("Trekking")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
            .WithSummary("Set or update customer identification (driver portal)")
            .Produces<SetCustomerIdDocument.IdDocumentResponse>(200)
            .Produces<Error>(422)
            .AllowAnonymous();
    }
}
