using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordStopReturn
{
    public class Command : IRequest<Result<ReturnResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid SaleInvoiceId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public PaymentMethod? RefundMethod { get; set; }
        public string? Reason { get; set; }
    }

    public class ReturnResponse
    {
        public Guid ReturnId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal RefundAmount { get; set; }
        public string? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public DateTime RecordedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.SaleInvoiceId).NotEmpty();
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.BasicQtyReturned).GreaterThan(0);
            RuleFor(x => x.PackagingQtyReturned).GreaterThan(0).When(x => x.PackagingQtyReturned.HasValue);
            RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<ReturnResponse>>
    {
        public async Task<Result<ReturnResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<ReturnResponse>(Error.ValidationError(validation));

            var stop = await db.TrekkingTripStops
                .Include(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == request.TrekId, cancellationToken);

            if (stop is null)
                return Result.Failure<ReturnResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (stop.TrekkingTrip.Status == TrekStatus.Completed || stop.TrekkingTrip.Status == TrekStatus.Cancelled)
                return Result.Failure<ReturnResponse>(Error.BadRequest($"Cannot record a return on a {stop.TrekkingTrip.Status} trek."));

            var invoice = await db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .FirstOrDefaultAsync(i => i.Id == request.SaleInvoiceId, cancellationToken);

            if (invoice is null)
                return Result.Failure<ReturnResponse>(Error.CreateNotFoundError("Invoice not found."));

            var lineItem = invoice.Stop.Products
                .FirstOrDefault(p => p.ProductId == request.ProductId);

            if (lineItem is null)
                return Result.Failure<ReturnResponse>(Error.BadRequest("Product not found on this invoice."));

            var refundAmount = request.BasicQtyReturned * lineItem.BasicUnitPrice
                + (request.PackagingQtyReturned ?? 0) * (lineItem.PackagingUnitPrice ?? 0);

            var userId = auth.GetUserId();
            Guid? staffId = Guid.TryParse(userId, out var uid) ? uid : null;

            var ret = new TrekkingTripStopReturn
            {
                TrekkingTripStopId = request.StopId,
                SaleInvoiceId = request.SaleInvoiceId,
                ProductId = request.ProductId,
                BasicQtyReturned = request.BasicQtyReturned,
                PackagingQtyReturned = request.PackagingQtyReturned,
                BasicUnitPrice = lineItem.BasicUnitPrice,
                PackagingUnitPrice = lineItem.PackagingUnitPrice,
                RefundAmount = refundAmount,
                RefundMethod = request.RefundMethod,
                Reason = request.Reason?.Trim(),
                ApprovalStatus = ReturnApprovalStatus.Pending,
                RecordedByStaffId = staffId,
                RecordedAt = DateTime.UtcNow
            };

            db.TrekkingTripStopReturns.Add(ret);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(ret, invoice.InvoiceNumber, lineItem.Product.Name));
        }

        internal static ReturnResponse ToResponse(TrekkingTripStopReturn r, string? invoiceNumber, string productName) => new()
        {
            ReturnId = r.Id,
            InvoiceNumber = invoiceNumber ?? string.Empty,
            ProductId = r.ProductId,
            ProductName = productName,
            BasicQtyReturned = r.BasicQtyReturned,
            PackagingQtyReturned = r.PackagingQtyReturned,
            BasicUnitPrice = r.BasicUnitPrice,
            PackagingUnitPrice = r.PackagingUnitPrice,
            RefundAmount = r.RefundAmount ?? 0,
            RefundMethod = r.RefundMethod?.ToString(),
            Reason = r.Reason,
            ApprovalStatus = r.ApprovalStatus.ToString(),
            RecordedAt = r.RecordedAt
        };
    }
}

public class RecordStopReturnEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/returns",
            async (Guid trekId, Guid stopId, RecordStopReturn.Command command, ISender sender) =>
            {
                command.TrekId = trekId;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/{trekId}/stops/{stopId}/returns/{result.Value.ReturnId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record a product return at a stop (admin)")
        .WithDescription("Creates a return against a specific invoice line item. Prices are sourced from the original invoice. Return is created as Pending and requires admin approval.")
        .Produces<RecordStopReturn.ReturnResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
