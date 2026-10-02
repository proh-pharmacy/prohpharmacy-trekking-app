using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RejectReturn
{
    public class Command : IRequest<Result<RejectionResponse>>
    {
        public Guid ReturnId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class RejectionResponse
    {
        public Guid ReturnId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string ApprovalStatus { get; set; } = string.Empty;
        public string RejectionReason { get; set; } = string.Empty;
        public DateTime RejectedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required when rejecting a return.")
                .MaximumLength(500);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<RejectionResponse>>
    {
        public async Task<Result<RejectionResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<RejectionResponse>(Error.ValidationError(validation));

            var ret = await db.TrekkingTripStopReturns
                .Include(r => r.SaleInvoice)
                .Include(r => r.Product)
                .Include(r => r.TrekkingTripStop)
                    .ThenInclude(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(r => r.Id == request.ReturnId, cancellationToken);

            if (ret is null)
                return Result.Failure<RejectionResponse>(Error.CreateNotFoundError("Return not found."));

            if (ret.ApprovalStatus != ReturnApprovalStatus.Pending)
                return Result.Failure<RejectionResponse>(Error.BadRequest($"Return is already {ret.ApprovalStatus}."));

            if (ret.TrekkingTripStop.TrekkingTrip.Status != TrekStatus.Completed)
                return Result.Failure<RejectionResponse>(Error.BadRequest("Return can only be rejected after the recording trek has been completed."));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<RejectionResponse>(
                    Error.BadRequest("Authenticated staff identity is missing or invalid."));

            ret.ApprovalStatus = ReturnApprovalStatus.Rejected;
            ret.ApprovedByStaffId = staffId;
            ret.ApprovedAt = DateTime.UtcNow;
            ret.RejectionReason = request.Reason.Trim();

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new RejectionResponse
            {
                ReturnId = ret.Id,
                InvoiceNumber = ret.SaleInvoice.InvoiceNumber ?? string.Empty,
                ProductName = ret.Product.Name,
                ApprovalStatus = ret.ApprovalStatus.ToString(),
                RejectionReason = ret.RejectionReason,
                RejectedAt = ret.ApprovedAt!.Value
            });
        }
    }
}

public class RejectReturnEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/returns/{returnId:guid}/reject",
            async (Guid returnId, RejectReturn.Command command, ISender sender) =>
            {
                command.ReturnId = returnId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Reject a pending product return")
        .WithDescription("Rejects the return with a required reason. No ledger impact.")
        .Produces<RejectReturn.RejectionResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
