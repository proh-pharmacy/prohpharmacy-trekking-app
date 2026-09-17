using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class SyncOfflineActionsByDriverToken
{
    public class Command : IRequest<Result<SyncResponse>>
    {
        public Guid Token { get; set; }
        public List<OfflineAction> Actions { get; set; } = [];
    }

    public class OfflineAction
    {
        public string Type { get; set; } = string.Empty;
        public Guid ClientId { get; set; }
        public DateTime OccurredAt { get; set; }
        public System.Text.Json.JsonElement Payload { get; set; }
    }

    public class SyncResponse
    {
        public List<ActionResult> Results { get; set; } = [];
    }

    public class ActionResult
    {
        public Guid ClientId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? ServerId { get; set; }
        public string? Reason { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<SyncResponse>>
    {
        public async Task<Result<SyncResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<SyncResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;
            var owningBranchId = (trip.SalesStaff ?? trip.Driver)?.BranchId ?? trip.Driver.BranchId;

            var ordered = request.Actions.OrderBy(a => a.OccurredAt).ToList();
            var results = new List<ActionResult>();

            // In-batch maps: clientId → server Guid for entities created in this batch
            var customerClientMap = new Dictionary<Guid, Guid>();
            var stopClientMap = new Dictionary<Guid, Guid>();

            foreach (var action in ordered)
            {
                var result = action.Type switch
                {
                    "RegisterCustomer" => await ProcessRegisterCustomerAsync(action, trip, attributedStaffId, owningBranchId, customerClientMap, cancellationToken),
                    "AddWalkInStop"    => await ProcessAddWalkInStopAsync(action, trip, customerClientMap, stopClientMap, cancellationToken),
                    "RecordUnplannedSale" => await ProcessUnplannedSaleAsync(action, trip, stopClientMap, cancellationToken),
                    "RecordReturn"     => await ProcessRecordReturnAsync(action, trip, attributedStaffId, stopClientMap, cancellationToken),
                    _ => new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = $"Unknown action type: {action.Type}" }
                };
                results.Add(result);
            }

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new SyncResponse { Results = results });
        }

        private async Task<ActionResult> ProcessRegisterCustomerAsync(
            OfflineAction action,
            Entities.TrekkingTrip trip,
            Guid attributedStaffId,
            Guid owningBranchId,
            Dictionary<Guid, Guid> customerClientMap,
            CancellationToken ct)
        {
            var existing = await db.CustomerAccounts
                .FirstOrDefaultAsync(c => c.ClientGeneratedId == action.ClientId, ct);
            if (existing is not null)
            {
                customerClientMap[action.ClientId] = existing.Id;
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "AlreadySynced", ServerId = existing.Id };
            }

            var payload = action.Payload;
            var businessName = payload.TryGetProperty("businessName", out var bn) ? bn.GetString() ?? string.Empty : string.Empty;
            var phone = payload.TryGetProperty("primaryPhoneNumber", out var ph) ? ph.GetString() ?? string.Empty : string.Empty;
            var customerTypeStr = payload.TryGetProperty("customerType", out var ct2) ? ct2.GetString() : null;

            if (string.IsNullOrWhiteSpace(businessName) || string.IsNullOrWhiteSpace(phone))
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "businessName and primaryPhoneNumber are required." };

            var phoneExists = await db.CustomerAccounts.AnyAsync(c => c.PrimaryPhoneNumber == phone.Trim(), ct);
            if (phoneExists)
            {
                var match = await db.CustomerAccounts.FirstAsync(c => c.PrimaryPhoneNumber == phone.Trim(), ct);
                customerClientMap[action.ClientId] = match.Id;
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "AlreadySynced", ServerId = match.Id };
            }

            if (!Enum.TryParse<CustomerType>(customerTypeStr, ignoreCase: true, out var customerType))
                customerType = CustomerType.RetailPharmacy;

            var count = await db.CustomerAccounts.CountAsync(c => c.RegionId == trip.RegionId, ct);
            var code = $"{trip.Region.Code.ToUpper()}-{(count + 1):D5}";

            var account = new CustomerAccount
            {
                CustomerCode = code,
                BusinessName = businessName.Trim(),
                CustomerType = customerType,
                RegionId = trip.RegionId,
                PrimaryPhoneNumber = phone.Trim(),
                OwningBranchId = owningBranchId,
                RegistrationStatus = RegistrationStatus.Active,
                RegisteredByStaffId = attributedStaffId,
                RegisteredDuringTrekId = trip.Id,
                ClientGeneratedId = action.ClientId,
                CreatedOffline = true,
                RecordedAt = action.OccurredAt,
                CreatedAt = DateTime.UtcNow
            };
            db.CustomerAccounts.Add(account);

            if (payload.TryGetProperty("representative", out var rep))
            {
                var firstName = rep.TryGetProperty("firstName", out var fn) ? fn.GetString()?.Trim() ?? string.Empty : string.Empty;
                var lastName = rep.TryGetProperty("lastName", out var ln) ? ln.GetString()?.Trim() ?? string.Empty : string.Empty;
                var repPhone = rep.TryGetProperty("primaryPhoneNumber", out var rph) ? rph.GetString()?.Trim() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
                {
                    db.CustomerPersons.Add(new CustomerPerson
                    {
                        CustomerAccountId = account.Id,
                        FirstName = firstName,
                        LastName = lastName,
                        RelationshipType = Features.Customers.Enums.RelationshipType.Owner,
                        PrimaryPhoneNumber = string.IsNullOrWhiteSpace(repPhone) ? phone.Trim() : repPhone,
                        IsPrimaryContact = true,
                        IsCreditResponsiblePerson = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (payload.TryGetProperty("gps", out var gps))
            {
                var lat = gps.TryGetProperty("latitude", out var la) ? (decimal?)la.GetDecimal() : null;
                var lon = gps.TryGetProperty("longitude", out var lo) ? (decimal?)lo.GetDecimal() : null;
                var acc = gps.TryGetProperty("accuracyMetres", out var am) ? (decimal?)am.GetDecimal() : null;
                if (lat.HasValue && lon.HasValue)
                {
                    db.CustomerLocations.Add(new CustomerLocation
                    {
                        CustomerAccountId = account.Id,
                        LocationType = LocationType.BusinessPremises,
                        RegionId = trip.RegionId,
                        Latitude = lat,
                        Longitude = lon,
                        AccuracyMetres = acc,
                        CaptureMethod = CaptureMethod.PwaGps,
                        VerificationStatus = LocationVerificationStatus.GpsCaptured,
                        IsPrimary = true,
                        CapturedByStaffId = attributedStaffId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            customerClientMap[action.ClientId] = account.Id;
            return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Created", ServerId = account.Id };
        }

        private async Task<ActionResult> ProcessAddWalkInStopAsync(
            OfflineAction action,
            Entities.TrekkingTrip trip,
            Dictionary<Guid, Guid> customerClientMap,
            Dictionary<Guid, Guid> stopClientMap,
            CancellationToken ct)
        {
            var existing = await db.TrekkingTripStops
                .FirstOrDefaultAsync(s => s.TrekkingTripId == trip.Id
                    && s.CustomerAccount.ClientGeneratedId == action.ClientId, ct);

            if (existing is null)
            {
                existing = await db.TrekkingTripStops
                    .FirstOrDefaultAsync(s => s.Id == action.ClientId, ct);
            }

            if (existing is not null)
            {
                stopClientMap[action.ClientId] = existing.Id;
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "AlreadySynced", ServerId = existing.Id };
            }

            var payload = action.Payload;

            Guid trekId = trip.Id;
            if (payload.TryGetProperty("trekId", out var tid) && Guid.TryParse(tid.GetString(), out var parsedTrekId))
                trekId = parsedTrekId;

            var targetTrip = await db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.Id == trekId && t.RegionId == trip.RegionId, ct);
            if (targetTrip is null || targetTrip.Status == TrekStatus.Completed || targetTrip.Status == TrekStatus.Cancelled)
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "Target trek not found, not in region, or is completed/cancelled." };

            Guid customerId;
            if (payload.TryGetProperty("customerClientId", out var ccid) && Guid.TryParse(ccid.GetString(), out var cClientId))
            {
                if (!customerClientMap.TryGetValue(cClientId, out customerId))
                {
                    var resolved = await db.CustomerAccounts.FirstOrDefaultAsync(c => c.ClientGeneratedId == cClientId, ct);
                    if (resolved is null)
                        return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "customerClientId could not be resolved to a customer." };
                    customerId = resolved.Id;
                }
            }
            else if (payload.TryGetProperty("customerId", out var cid) && Guid.TryParse(cid.GetString(), out var directId))
            {
                customerId = directId;
            }
            else
            {
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "customerId or customerClientId is required." };
            }

            var seq = payload.TryGetProperty("sequence", out var seqEl) ? seqEl.GetInt32() : 1;
            var notes = payload.TryGetProperty("notes", out var n) ? n.GetString() : null;

            var stop = new TrekkingTripStop
            {
                TrekkingTripId = trekId,
                CustomerAccountId = customerId,
                Sequence = seq,
                IsWalkIn = true,
                Notes = notes?.Trim()
            };
            db.TrekkingTripStops.Add(stop);

            stopClientMap[action.ClientId] = stop.Id;
            return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Created", ServerId = stop.Id };
        }

        private async Task<ActionResult> ProcessUnplannedSaleAsync(
            OfflineAction action,
            Entities.TrekkingTrip trip,
            Dictionary<Guid, Guid> stopClientMap,
            CancellationToken ct)
        {
            var existing = await db.TrekkingTripStopProducts
                .FirstOrDefaultAsync(p => p.Id == action.ClientId, ct);
            if (existing is not null)
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "AlreadySynced", ServerId = existing.Id };

            var payload = action.Payload;

            Guid stopId;
            if (payload.TryGetProperty("stopClientId", out var scid) && Guid.TryParse(scid.GetString(), out var stopClientId))
            {
                if (!stopClientMap.TryGetValue(stopClientId, out stopId))
                    return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "stopClientId could not be resolved to a stop." };
            }
            else if (payload.TryGetProperty("stopId", out var sid) && Guid.TryParse(sid.GetString(), out var directStopId))
            {
                stopId = directStopId;
            }
            else
            {
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "stopId or stopClientId is required." };
            }

            if (!payload.TryGetProperty("productId", out var pidEl) || !Guid.TryParse(pidEl.GetString(), out var productId))
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "productId is required." };

            var product = await db.Products.FindAsync([productId], ct);
            if (product is null)
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "Product not found." };

            var basicQty = payload.TryGetProperty("basicQtyDelivered", out var bq) ? bq.GetDecimal() : 0;
            var pkgQty = payload.TryGetProperty("packagingQtyDelivered", out var pq) ? (decimal?)pq.GetDecimal() : null;
            var pmStr = payload.TryGetProperty("paymentMethod", out var pm) ? pm.GetString() : null;
            Enum.TryParse<PaymentMethod>(pmStr, ignoreCase: true, out var paymentMethod);
            var amtPaid = payload.TryGetProperty("amtPaid", out var ap) ? (decimal?)ap.GetDecimal() : null;
            var balance = payload.TryGetProperty("balance", out var bal) ? (decimal?)bal.GetDecimal() : null;

            db.TrekkingTripStopProducts.Add(new TrekkingTripStopProduct
            {
                TrekkingTripStopId = stopId,
                ProductId = productId,
                PlannedBasicQuantity = 0,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = product.PackagingUnitId.HasValue ? product.PackagingUnitPrice : null,
                BasicQtyDelivered = basicQty,
                PackagingQtyDelivered = product.PackagingUnitId.HasValue ? pkgQty : null,
                PaymentMethod = pmStr is not null ? paymentMethod : null,
                AmtPaid = amtPaid,
                Balance = balance,
                IsUnplanned = true,
                DeliveredAt = action.OccurredAt
            });

            return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Created" };
        }

        private async Task<ActionResult> ProcessRecordReturnAsync(
            OfflineAction action,
            Entities.TrekkingTrip trip,
            Guid attributedStaffId,
            Dictionary<Guid, Guid> stopClientMap,
            CancellationToken ct)
        {
            var existing = await db.TrekkingTripStopReturns
                .FirstOrDefaultAsync(r => r.ClientGeneratedId == action.ClientId, ct);
            if (existing is not null)
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "AlreadySynced", ServerId = existing.Id };

            var payload = action.Payload;

            Guid stopId;
            if (payload.TryGetProperty("stopClientId", out var scid) && Guid.TryParse(scid.GetString(), out var stopClientId))
            {
                if (!stopClientMap.TryGetValue(stopClientId, out stopId))
                    return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "stopClientId could not be resolved to a stop." };
            }
            else if (payload.TryGetProperty("stopId", out var sid) && Guid.TryParse(sid.GetString(), out var directStopId))
            {
                stopId = directStopId;
            }
            else
            {
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "stopId or stopClientId is required." };
            }

            if (!payload.TryGetProperty("productId", out var pidEl) || !Guid.TryParse(pidEl.GetString(), out var productId))
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "productId is required." };

            var product = await db.Products.FindAsync([productId], ct);
            if (product is null)
                return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Conflict", Reason = "Product not found." };

            var basicQty = payload.TryGetProperty("basicQtyReturned", out var bq) ? bq.GetDecimal() : 0;
            var pkgQty = payload.TryGetProperty("packagingQtyReturned", out var pq) ? (decimal?)pq.GetDecimal() : null;
            var refundAmount = payload.TryGetProperty("refundAmount", out var ra) ? (decimal?)ra.GetDecimal() : null;
            var refundMethodStr = payload.TryGetProperty("refundMethod", out var rm) ? rm.GetString() : null;
            Enum.TryParse<PaymentMethod>(refundMethodStr, ignoreCase: true, out var refundMethod);
            var reason = payload.TryGetProperty("reason", out var rs) ? rs.GetString() : null;

            decimal? lat = null, lon = null, acc = null;
            if (payload.TryGetProperty("gps", out var gps))
            {
                lat = gps.TryGetProperty("latitude", out var la) ? (decimal?)la.GetDecimal() : null;
                lon = gps.TryGetProperty("longitude", out var lo) ? (decimal?)lo.GetDecimal() : null;
                acc = gps.TryGetProperty("accuracyMetres", out var am) ? (decimal?)am.GetDecimal() : null;
            }

            var ret = new TrekkingTripStopReturn
            {
                TrekkingTripStopId = stopId,
                ProductId = productId,
                BasicQtyReturned = basicQty,
                PackagingQtyReturned = product.PackagingUnitId.HasValue ? pkgQty : null,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = product.PackagingUnitId.HasValue ? product.PackagingUnitPrice : null,
                RefundAmount = refundAmount,
                RefundMethod = refundMethodStr is not null ? refundMethod : null,
                Reason = reason?.Trim(),
                RecordedByStaffId = attributedStaffId,
                ClientGeneratedId = action.ClientId,
                Latitude = lat,
                Longitude = lon,
                GpsAccuracyMetres = acc,
                RecordedAt = action.OccurredAt
            };
            db.TrekkingTripStopReturns.Add(ret);

            return new ActionResult { ClientId = action.ClientId, Type = action.Type, Status = "Created", ServerId = ret.Id };
        }
    }
}

public class SyncOfflineActionsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/sync",
            async (Guid token, SyncOfflineActionsByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Push queued offline actions in a batch (driver portal)")
        .WithDescription("Actions are processed in occurredAt order. Idempotent — re-submitting the same batch is safe. Action types: RegisterCustomer, AddWalkInStop, RecordUnplannedSale, RecordReturn.")
        .Produces<SyncOfflineActionsByDriverToken.SyncResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
