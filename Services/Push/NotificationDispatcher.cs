using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Notifications.Entities;

namespace prohpharmacy_trekking_app.Services.Push;

public class NotificationDispatcher(AppDbContext db, IWebPushService push)
{
    public async Task DispatchAsync(
        string type,
        string title,
        string message,
        string? data,
        IEnumerable<Guid> recipientIds,
        CancellationToken cancellationToken = default)
    {
        var recipients = recipientIds.Distinct().ToList();
        if (recipients.Count == 0) return;

        var now = DateTime.UtcNow;
        var notifications = recipients.Select(id => new AppNotification
        {
            Type = type,
            Title = title,
            Message = message,
            Data = data,
            NotifiableType = "StaffMember",
            NotifiableId = id,
            CreatedAt = now
        }).ToList();

        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(cancellationToken);

        var subscriptions = await db.StaffPushSubscriptions
            .Where(s => recipients.Contains(s.StaffMemberId))
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0) return;

        var pushPayload = JsonSerializer.Serialize(new { title, body = message, data });

        var staleEndpoints = new List<Guid>();
        foreach (var sub in subscriptions)
        {
            try
            {
                await push.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, pushPayload);
            }
            catch (WebPush.WebPushException ex) when ((int)ex.StatusCode == 410)
            {
                staleEndpoints.Add(sub.Id);
            }
            catch
            {
                // ignore transient failures
            }
        }

        if (staleEndpoints.Count > 0)
        {
            await db.StaffPushSubscriptions
                .Where(s => staleEndpoints.Contains(s.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
