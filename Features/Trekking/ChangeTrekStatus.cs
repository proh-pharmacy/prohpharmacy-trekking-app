using Carter;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Ledger.Entities;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Services.Jobs;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Services.Push;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ChangeTrekStatus
{
    public class Command : IRequest<Result<TrekResponse>>
    {
        public Guid Id { get; set; }
        public TrekStatus Status { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Status).IsInEnum();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly IBackgroundJobClient _jobs;
        private readonly NotificationDispatcher _notifications;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public Handler(AppDbContext db, IValidator<Command> validator, IBackgroundJobClient jobs, NotificationDispatcher notifications, IEmailService email, IConfiguration config)
        {
            _db = db;
            _validator = validator;
            _jobs = jobs;
            _notifications = notifications;
            _email = email;
            _config = config;
        }

        public async Task<Result<TrekResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekResponse>(Error.ValidationError(validation));

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var trip = await _db.TrekkingTrips
                    .Include(t => t.Region)
                    .Include(t => t.Branch)
                    .Include(t => t.Driver)
                    .Include(t => t.SalesStaff)
                    .Include(t => t.Vehicle)
                    .Include(t => t.Stops)
                        .ThenInclude(s => s.Products)
                    .Include(t => t.Stops)
                        .ThenInclude(s => s.Returns)
                    .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

                if (trip is null)
                    return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

                trip.Status = request.Status;
                trip.UpdatedAt = DateTime.UtcNow;

                if (request.Status == TrekStatus.Scheduled)
                    trip.DriverToken ??= Guid.NewGuid();
                else if (request.Status == TrekStatus.InProgress)
                    trip.DriverToken ??= Guid.NewGuid();
                else if (request.Status == TrekStatus.Completed)
                {
                    await SyncLedgerOnCompletionAsync(trip, cancellationToken);
                    await CompleteTrekByDriverToken.Handler.CaptureStockSnapshotAsync(_db, trip, cancellationToken);
                    await SyncVehicleStockOnCompletionAsync(trip, cancellationToken);
                }
                else if (request.Status == TrekStatus.Cancelled)
                    await ClearTrekLedgerEntriesAsync(trip.Id, cancellationToken);

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                if (request.Status == TrekStatus.Scheduled)
                    _jobs.Enqueue<TrekEmailJob>(j => j.SendTrekStartEmailsAsync(trip.Id, trip.DriverToken!.Value));

                DispatchTrekNotificationAsync(trip, request.Status);

                if (request.Status == TrekStatus.Completed)
                    await NotifyPendingReturnsAsync(trip, cancellationToken);

                return Result.Success(CreateTrek.Handler.ToResponse(
                    trip,
                    trip.Region?.Name ?? string.Empty,
                    trip.Branch?.Name,
                    trip.Driver?.FullName ?? string.Empty,
                    trip.SalesStaff?.FullName,
                    trip.Vehicle?.DisplayName ?? string.Empty,
                    []));
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private void DispatchTrekNotificationAsync(Entities.TrekkingTrip trip, TrekStatus status)
        {
            var (type, title, message) = status switch
            {
                TrekStatus.Scheduled  => ("TrekAssigned",  "Trek Assigned",  $"Trek {trip.TrekNumber} has been assigned to you on {trip.ScheduledDate:dd MMM yyyy}."),
                TrekStatus.InProgress => ("TrekStarted",   "Trek Started",   $"Trek {trip.TrekNumber} is now in progress."),
                TrekStatus.Completed  => ("TrekCompleted", "Trek Completed", $"Trek {trip.TrekNumber} has been completed."),
                TrekStatus.Cancelled  => ("TrekCancelled", "Trek Cancelled", $"Trek {trip.TrekNumber} has been cancelled."),
                _                     => (null, null, null)
            };

            if (type is null) return;

            var recipients = new List<Guid> { trip.DriverStaffId };
            if (trip.SalesStaffId.HasValue && trip.SalesStaffId.Value != trip.DriverStaffId)
                recipients.Add(trip.SalesStaffId.Value);

            var data = System.Text.Json.JsonSerializer.Serialize(new { trekId = trip.Id, trekNumber = trip.TrekNumber });
            _ = _notifications.DispatchAsync(type, title!, message!, data, recipients);
        }

        private async Task SyncLedgerOnCompletionAsync(Entities.TrekkingTrip trip, CancellationToken cancellationToken)
        {
            var stopIds = trip.Stops.Select(s => s.Id).ToList();

            var existing = await _db.CustomerLedgerEntries
                .Where(e => e.TrekkingTripStopId.HasValue
                    && stopIds.Contains(e.TrekkingTripStopId.Value)
                    && e.IsAutoGenerated)
                .ToListAsync(cancellationToken);
            _db.CustomerLedgerEntries.RemoveRange(existing);

            var invoiceNumbersByStop = await _db.SaleInvoices
                .Where(i => stopIds.Contains(i.TrekkingTripStopId) && i.InvoiceNumber != null)
                .ToDictionaryAsync(i => i.TrekkingTripStopId, i => i.InvoiceNumber!, cancellationToken);

            var now = DateTime.UtcNow;
            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;

            foreach (var stop in trip.Stops)
            {
                var stopTotal = stop.Products.Sum(p =>
                    p.AmountDue ?? ((p.AmtPaid ?? 0) + (p.Balance ?? 0)));
                invoiceNumbersByStop.TryGetValue(stop.Id, out var invoiceNumber);

                if (stopTotal > 0)
                    _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
                    {
                        CustomerAccountId = stop.CustomerAccountId,
                        EntryType = LedgerEntryType.Debit,
                        Amount = stopTotal,
                        Description = invoiceNumber is not null
                            ? $"Invoice {invoiceNumber} — Goods delivered (Trek {trip.TrekNumber})"
                            : $"Goods delivered — Trek {trip.TrekNumber}",
                        TrekkingTripId = trip.Id,
                        TrekkingTripStopId = stop.Id,
                        IsAutoGenerated = true,
                        RecordedAt = now,
                        CreatedByStaffId = attributedStaffId,
                        CreatedAt = now
                    });

                var paymentGroups = stop.Products
                    .Where(p => p.AmtPaid > 0 && p.PaymentMethod.HasValue)
                    .GroupBy(p => p.PaymentMethod!.Value);

                foreach (var group in paymentGroups)
                {
                    var amtPaid = group.Sum(p => p.AmtPaid ?? 0);
                    _db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
                    {
                        CustomerAccountId = stop.CustomerAccountId,
                        EntryType = LedgerEntryType.Credit,
                        Amount = amtPaid,
                        PaymentMethod = group.Key.ToString(),
                        Description = invoiceNumber is not null
                            ? $"Invoice {invoiceNumber} — Payment received: {group.Key} (Trek {trip.TrekNumber})"
                            : $"Payment received ({group.Key}) — Trek {trip.TrekNumber}",
                        TrekkingTripId = trip.Id,
                        TrekkingTripStopId = stop.Id,
                        IsAutoGenerated = true,
                        RecordedAt = now,
                        CreatedByStaffId = attributedStaffId,
                        CreatedAt = now
                    });
                }
            }
        }

        private async Task SyncVehicleStockOnCompletionAsync(Entities.TrekkingTrip trip, CancellationToken cancellationToken)
        {
            var deliveredByProduct = trip.Stops
                .SelectMany(s => s.Products)
                .Where(p => p.BasicQtyDelivered.HasValue && p.BasicQtyDelivered > 0)
                .GroupBy(p => p.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    BasicDelivered = g.Sum(p => p.BasicQtyDelivered ?? 0),
                    PackagingDelivered = g.Sum(p => p.PackagingQtyDelivered ?? 0)
                })
                .ToList();

            if (deliveredByProduct.Count == 0) return;

            var productIds = deliveredByProduct.Select(d => d.ProductId).ToList();

            var stockRecords = await _db.VehicleProductStocks
                .Where(s => s.VehicleId == trip.VehicleId && productIds.Contains(s.ProductId))
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;

            foreach (var delivery in deliveredByProduct)
            {
                var stock = stockRecords.FirstOrDefault(s => s.ProductId == delivery.ProductId);
                if (stock is null) continue;

                var basicDeducted = Math.Min(delivery.BasicDelivered, stock.BasicQuantityOnHand);
                var packagingDeducted = Math.Min(delivery.PackagingDelivered, stock.PackagingQuantityOnHand);

                stock.BasicQuantityOnHand = Math.Max(0, stock.BasicQuantityOnHand - delivery.BasicDelivered);
                stock.PackagingQuantityOnHand = Math.Max(0, stock.PackagingQuantityOnHand - delivery.PackagingDelivered);
                stock.UpdatedAt = now;

                _db.VehicleStockLedger.Add(new VehicleStockLedger
                {
                    VehicleId = trip.VehicleId,
                    ProductId = delivery.ProductId,
                    ChangeType = StockChangeType.Reduction,
                    Source = StockChangeSource.TrekCompletion,
                    BasicQtyChange = basicDeducted,
                    PackagingQtyChange = packagingDeducted,
                    BasicBalanceAfter = stock.BasicQuantityOnHand,
                    PackagingBalanceAfter = stock.PackagingQuantityOnHand,
                    Reason = $"Trek {trip.TrekNumber} completed — {basicDeducted:0.###} units delivered",
                    ReferenceId = trip.Id,
                    AuthorStaffId = null,
                    RecordedAt = now
                });
            }
        }

        private async Task NotifyPendingReturnsAsync(Entities.TrekkingTrip trip, CancellationToken cancellationToken)
        {
            var pendingCount = trip.Stops
                .SelectMany(s => s.Returns)
                .Count(r => r.ApprovalStatus == ReturnApprovalStatus.Pending);

            if (pendingCount == 0) return;

            var creator = await _db.StaffMembers
                .Where(s => s.ApplicationUser!.Id == trip.CreatedBy)
                .Select(s => new { s.Id, s.FullName, s.EmailAddress })
                .FirstOrDefaultAsync(cancellationToken);

            if (creator is null) return;

            var data = System.Text.Json.JsonSerializer.Serialize(new { trekId = trip.Id, trekNumber = trip.TrekNumber });
            _ = _notifications.DispatchAsync(
                "PendingReturns",
                "Pending Returns",
                $"Trek {trip.TrekNumber} has been completed with {pendingCount} pending return(s) awaiting your approval.",
                data,
                [creator.Id]);

            var frontendUrl = _config["SiteSettings:FrontendUrl"] ?? string.Empty;
            var appName = _config["SiteSettings:AppName"] ?? "Proh Pharmacy";
            var supportEmail = _config["EmailSettings:SupportEmail"] ?? string.Empty;

            _ = _email.SendPendingReturnsNotificationAsync(creator.EmailAddress, new PendingReturnsNotificationEmailModel
            {
                RecipientName = creator.FullName,
                TrekNumber = trip.TrekNumber ?? string.Empty,
                CompletedDate = DateTime.UtcNow.ToString("dd MMM yyyy"),
                PendingReturnCount = pendingCount,
                ReviewUrl = $"{frontendUrl}/treks/{trip.Id}",
                AppName = appName,
                SupportEmail = supportEmail
            });
        }

        private async Task ClearTrekLedgerEntriesAsync(Guid trekId, CancellationToken cancellationToken)
        {
            var existing = await _db.CustomerLedgerEntries
                .Where(e => e.TrekkingTripId == trekId && e.IsAutoGenerated)
                .ToListAsync(cancellationToken);
            _db.CustomerLedgerEntries.RemoveRange(existing);
        }
    }
}

public class ChangeTrekStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/treks/{id:guid}/status", async (Guid id, ChangeTrekStatus.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Change the status of a trekking trip")
        .WithDescription("Valid statuses: Draft, Scheduled, InProgress, Completed, Cancelled.")
        .Produces<TrekResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
