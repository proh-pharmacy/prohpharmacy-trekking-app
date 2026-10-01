# Phase 3 — Returns Against Invoices (with Approval Workflow)

## Goal
Allow drivers to record product returns against a specific historical invoice during any trek visit.
Returns are not applied automatically — they require admin approval after the trek is completed.

---

## How returns work (the real flow)

1. A customer bought products in **Trek A** → a `SaleInvoice` was generated
2. Weeks or months later during **Trek B**, the driver visits that customer
3. The customer wants to return one or more items from Trek A's invoice
4. The driver (online only) looks up the customer's invoice by number or searches their invoice history
5. The system lists the line items on that invoice
6. The driver selects which items to return and how many — **per item, not the whole invoice**
7. Refund amount is **auto-calculated** from the original invoice line item price × return quantity
8. A single stop can have returns against **multiple different invoices**
9. Returns are saved as **Pending** — nothing applied to the ledger yet
10. The `TrekkingTripStopId` records *where physically* the return was captured (current trek stop)
11. When **Trek B is completed**, the admin who created it is notified of any pending returns
12. Admin reviews and **approves or rejects** each return individually
13. Only on approval does a ledger credit entry get created

---

## Why the existing implementation was wrong

- `RecordStopReturn` pulled prices from the product catalogue — not the original invoice
- No `SaleInvoiceId` link — no traceability to which invoice was affected
- No approval workflow — returns were applied immediately
- Return creation allowed offline (`ClientGeneratedId`) — must be online only since invoice lookup requires connectivity

---

## Changes to `TrekkingTripStopReturn` entity ✅

### New fields added

| Field | Type | Notes |
|---|---|---|
| `SaleInvoiceId` | `Guid` | FK to `SaleInvoice` — required |
| `ApprovalStatus` | `ReturnApprovalStatus` (enum) | Default `Pending` |
| `ApprovedByStaffId` | `Guid?` | Who approved/rejected |
| `ApprovedAt` | `DateTime?` | When approved/rejected |
| `RejectionReason` | `string?` | Required on rejection |

`ClientGeneratedId` retained on entity for backwards compat but no longer used by new endpoints.
Prices and `RefundAmount` always sourced from the invoice line item — never manually entered.

---

## New enum: `ReturnApprovalStatus` ✅

```
Pending    — recorded by driver, awaiting admin review
Approved   — admin confirmed, ledger credit applied
Rejected   — admin rejected, no ledger impact
```

Location: `Features/Trekking/Enums/ReturnApprovalStatus.cs`

---

## Rewritten endpoints ✅

### `RecordStopReturn` — `POST /api/v1/treks/{trekId}/stops/{stopId}/returns`
Takes `SaleInvoiceId` + `ProductId` (must be a line item on that invoice). Prices auto-filled from invoice. Created as `Pending`.

### `RecordStopReturnByDriverToken` — `POST /api/v1/treks/driver/{token}/stops/{stopId}/returns`
Same as admin side. Driver bounded to their region via token. **Online only.**

---

## New endpoints ✅

| Method | Route | Status | Description |
|---|---|---|---|
| `POST` | `/api/v1/returns/{returnId}/approve` | ✅ Done | Admin approves — creates ledger credit |
| `POST` | `/api/v1/returns/{returnId}/reject` | ✅ Done | Admin rejects with required reason |
| `GET` | `/api/v1/treks/{trekId}/returns` | ✅ Done | List returns on a trek, filterable by approval status |
| `GET` | `/api/v1/invoices/{invoiceNumber}/returns` | ✅ Done | List returns against a specific invoice |

---

## Trek completion notification ✅

When a trek is marked `Completed` (via `ChangeTrekStatus` or `CompleteTrekByDriverToken`), check if the trek has any `Pending` returns. If yes, notify the admin who created the trek.

**Notification message:**
> "Trek TRK-00012 has been completed with X pending return(s) awaiting your approval."

**Files to update:**
- `Features/Trekking/ChangeTrekStatus.cs` — add pending returns check + notification after status set to Completed
- `Features/Trekking/CompleteTrekByDriverToken.cs` — same

**How to implement:**
After saving the `Completed` status, count `TrekkingTripStopReturns` where `TrekkingTripStop.TrekkingTripId == tripId` and `ApprovalStatus == Pending`. If count > 0, fire a notification to the staff member identified by `trip.CreatedBy`.

---

## Approval side effects ✅

On **approve**: creates a `CustomerLedgerEntry` (Credit) for the refund amount, linked to the trek, stop, and customer.

On **reject**: no ledger impact. Return status set to `Rejected` with reason.

---

## Migration ✅

`AddReturnApprovalWorkflow` — adds `SaleInvoiceId`, `ApprovalStatus`, `ApprovedByStaffId`, `ApprovedAt`, `RejectionReason` to `TrekkingTripStopReturns`.
