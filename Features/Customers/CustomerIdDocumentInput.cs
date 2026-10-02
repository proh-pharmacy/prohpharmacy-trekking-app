using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Customers.Enums;

namespace prohpharmacy_trekking_app.Features.Customers;

internal static class CustomerIdDocumentInput
{
    // Omit both fields to preserve existing details. When changing the document,
    // require the pair so a new number cannot silently retain the wrong type.
    internal static string? Read(JsonElement payload, out bool supplied,
        out CustomerIdDocumentType? type, out string? number)
    {
        var hasType = payload.TryGetProperty("idDocumentType", out var typeValue);
        var hasNumber = payload.TryGetProperty("idDocumentNumber", out var numberValue);
        supplied = hasType || hasNumber;
        type = null;
        number = null;
        if (!supplied) return null;

        if (!hasType || !hasNumber || typeValue.ValueKind != JsonValueKind.String
            || numberValue.ValueKind != JsonValueKind.String)
            return "Supply both idDocumentType and idDocumentNumber as non-null strings.";

        var name = typeValue.GetString();
        if (!Enum.GetNames<CustomerIdDocumentType>().Contains(name, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse<CustomerIdDocumentType>(name, true, out var parsed))
            return "Invalid idDocumentType.";

        type = parsed;
        number = numberValue.GetString()?.Trim();
        return Validate(type, number);
    }

    internal static string? Validate(CustomerIdDocumentType? type, string? number)
    {
        if (!type.HasValue || !Enum.IsDefined(type.Value)) return "Invalid idDocumentType.";
        if (string.IsNullOrWhiteSpace(number)) return "Document number is required.";
        if (number.Trim().Length > 100) return "Document number must be 100 characters or fewer.";
        return null;
    }

    internal static async Task<bool> IsDuplicateAsync(AppDbContext db, Guid customerId,
        string number, CancellationToken ct)
    {
        // SaveChanges runs at the end of driver batch sync, so include pending
        // inserts/updates, not just the persisted database snapshot.
        if (db.CustomerAccounts.Local.Any(c => c.Id != customerId && c.IdDocumentNumber == number))
            return true;

        var matches = await db.CustomerAccounts.AsNoTracking()
            .Where(c => c.Id != customerId && c.IdDocumentNumber == number)
            .Select(c => c.Id).ToListAsync(ct);
        return matches.Any(id => !db.CustomerAccounts.Local.Any(c =>
            c.Id == id && c.IdDocumentNumber != number));
    }
}
