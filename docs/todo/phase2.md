# Phase 2 — Sale Invoices

## Goal
Every delivery at a trek stop generates a sale invoice with a unique sequential number. The backend owns the invoice record and number; the frontend renders the PDF.

---

## Context

Currently sales are embedded in `TrekkingTripStopProduct` — amount due, amount paid, balance, payment method are all there. No invoice entity exists. An invoice wraps a `TrekkingTripStop` and aggregates all products delivered at that stop.

---

## New entity: `SaleInvoice`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `InvoiceNumber` | `string` | Sequential — e.g. `INV-00001` |
| `TrekkingTripStopId` | `Guid` | FK — the stop this invoice is for |
| `TrekkingTripId` | `Guid` | Denormalised for easy querying |
| `CustomerAccountId` | `Guid` | Denormalised for easy querying |
| `IssuedAt` | `DateTime` | When invoice was generated |
| `TotalAmount` | `decimal` | Sum of all line items |
| `TotalPaid` | `decimal` | Total paid at time of generation |
| `Balance` | `decimal` | Outstanding amount |
| `Status` | `SaleInvoiceStatus` (enum) | See below |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime?` | |

Location: `Features/Trekking/Entities/SaleInvoice.cs`

---

## New enum: `SaleInvoiceStatus`

```
Issued       — generated at point of delivery
PartiallyPaid
Paid
Voided
```

Location: `Features/Trekking/Enums/SaleInvoiceStatus.cs`

---

## Invoice number sequence

Same pattern as `TrekNumber` (`TRK-00001`). Backend auto-generates on creation using `MAX(InvoiceNumber) + 1` padded to 5 digits with `INV-` prefix.

---

## When is an invoice created?

At the point delivery is recorded at a stop (`RecordTrekDelivery`). Not at trek completion. This satisfies the client requirement that invoices are generated per-sale even if the trek is still in progress.

---

## New endpoints needed

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/invoices/{invoiceNumber}` | Get a single invoice with line items |
| `GET` | `/api/v1/invoices` | List invoices (paginated, filterable by customer, trek, date, status) |
| `GET` | `/api/v1/treks/{trekId}/stops/{stopId}/invoice` | Get the invoice for a specific stop |

Invoice line items are served from `TrekkingTripStopProduct` — no separate line item entity needed.

---

## Migration

New migration: `AddSaleInvoice`

---

## Open questions

- [ ] One invoice per stop (all products at that stop on one invoice) — confirmed approach?
- [ ] What happens if more products are added to a stop after the invoice is issued (unplanned additions)? Does the invoice get amended, or a second invoice generated?
- [ ] Invoice numbering: global sequence across all branches, or per-branch?
