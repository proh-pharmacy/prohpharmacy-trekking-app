# 14 — Customer Ledger

> Updated for the invoice-first backend on 2026-11-08. See [Invoice First & Payments](./27-invoice-first-payments.md) for the payment and receipt contracts.


Return approval now creates an auto-generated Credit entry for the invoice customer, linked to the capturing trek/stop. Pending and rejected returns have no ledger impact. Approval does not amend invoice totals. Review returns after completing the trek, then refresh ledger/balance views. See [Invoice Returns & Approval](./23-invoice-returns.md).

## Overview

Every customer has a ledger — a running record of debits (amounts owed) and credits (payments received). Entries come from two sources:

- **Auto-generated** — scheduling creates an invoice debit for each planned stop's full billable total; active walk-in invoices create theirs immediately. Later item changes update that debit. Each invoice payment creates a separate credit. Approved returns create separate credits. Completion does not rebuild these entries.
- **Manual** — added by staff directly via `POST /api/v1/customers/{customerId}/ledger`. These are never auto-modified.

When a trek is `Cancelled`, all auto-generated entries tied to that trek are removed from the ledger.

> **Balance formula:** `currentBalance = totalDebits − totalCredits`. A positive balance means the customer owes money.

---

## Endpoints

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `GET` | `api/v1/ledger/summary` | Required | All customers with balances (debtors list) |
| `GET` | `api/v1/ledger/export` | Required | Download customer balance report as Excel (.xlsx) |
| `GET` | `api/v1/customers/{customerId}/ledger` | Required | Get a single customer's ledger with running totals |
| `GET` | `api/v1/customers/{customerId}/ledger/export` | Required | Download individual customer ledger statement as Excel (.xlsx) |
| `POST` | `api/v1/customers/{customerId}/ledger` | Required | Manually add a debit or credit entry |

---

## GET /api/v1/ledger/summary

Returns a paginated list of all customers with their pre-calculated debit, credit, and outstanding balance totals. Use this to build a debtors page — filter to customers who owe money, sort by balance, and drill into any individual customer's ledger from there.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `hasBalance` | `bool?` | `true` = only customers with a positive outstanding balance |
| `search` | `string?` | Search by business name or customer code |
| `sort` | `string?` | `balance_desc` (default), `balance_asc`, `name_asc`, `name_desc` |
| `branchId` | `guid?` | Filter by owning branch |
| `regionId` | `guid?` | Filter by region |
| `pageNumber` | `int?` | Defaults to `1` |
| `pageSize` | `int?` | Defaults to `20`, max `100` |

### Response `200 OK`

```json
{
  "totalOutstanding": 4250.00,
  "customersWithBalance": 12,
  "page": 1,
  "pageSize": 20,
  "totalCount": 12,
  "totalPages": 1,
  "customers": [
    {
      "customerId": "...",
      "customerCode": "GAR-00001",
      "businessName": "Tema Central Pharmacy",
      "primaryPhoneNumber": "+233244123456",
      "regionName": "Greater Accra",
      "branchName": "Tema Branch",
      "totalDebits": 850.00,
      "totalCredits": 600.00,
      "currentBalance": 250.00
    }
  ]
}
```

### Field notes

| Field | Notes |
|---|---|
| `totalOutstanding` | Sum of `currentBalance` across **all** customers with a positive balance — not affected by pagination or `hasBalance` filter, but does respect `branchId`, `regionId`, and `search`. |
| `customersWithBalance` | Count of customers with a positive balance — same scope as `totalOutstanding`. |
| `currentBalance` | `totalDebits − totalCredits`. Positive = customer owes money. Zero or negative = no outstanding debt. |

---

## GET /api/v1/ledger/export

Downloads a styled `.xlsx` Excel report of customer balances for a given date range. Entries are filtered by their `recordedAt` date, so totals reflect only what was recorded within the specified period.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Start date (inclusive), e.g. `2026-01-01` |
| `to` | `date?` | End date (inclusive), e.g. `2026-09-30` |
| `hasBalance` | `bool?` | `true` = only customers with a positive outstanding balance |
| `branchId` | `guid?` | Filter by owning branch |
| `regionId` | `guid?` | Filter by region |

Returns a file download named `LedgerSummary-{YYYYMMDD}.xlsx`. The sheet includes:
- Report title and period label
- One row per customer: Customer Code, Business Name, Region, Branch, Total Debits, Total Credits, Outstanding Balance
- Totals row at the bottom
- Outstanding balances highlighted in red
- Rows sorted highest balance first

### Usage

These endpoints require auth, so you must use `fetch` with the Bearer token rather than a plain `<a href>` link. Read the filename from the `Content-Disposition` response header (exposed via CORS).

```js
const response = await fetch(
  '/api/v1/ledger/export?from=2026-01-01&to=2026-09-30&hasBalance=true',
  { headers: { Authorization: `Bearer ${token}` } }
);
const disposition = response.headers.get('Content-Disposition');
const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? 'LedgerSummary.xlsx';
const blob = await response.blob();
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url; a.download = filename; a.click();
URL.revokeObjectURL(url);
```

---

## GET /api/v1/customers/{customerId}/ledger/export

Downloads a styled `.xlsx` Excel statement for a single customer. Entries are filtered by their `recordedAt` date. Both date parameters are optional — omit both to export the full history.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Start date (inclusive), e.g. `2026-01-01` |
| `to` | `date?` | End date (inclusive), e.g. `2026-09-30` |

Returns a file download named `Ledger-{CustomerCode}-{YYYYMMDD}.xlsx`. The sheet includes:
- Customer info block: name, code, phone, region, branch, period label, generated timestamp
- Entry table: Date, Type, Payment Method, Description, Trek No., Debit (GHS), Credit (GHS), Recorded By
  - Debit amounts appear in the Debit column (red), Credit amounts in the Credit column (green)
- Summary block: Total Debits, Total Credits, Outstanding Balance
  - Outstanding Balance highlighted red if positive (customer owes money), green if zero/paid up

### Usage

Same fetch pattern as `/api/v1/ledger/export` — send the Bearer token and read the filename from `Content-Disposition`. The filename is `Ledger-{CustomerCode}-{YYYYMMDD}.xlsx`.

```js
const response = await fetch(
  `/api/v1/customers/${customerId}/ledger/export?from=2026-01-01&to=2026-09-30`,
  { headers: { Authorization: `Bearer ${token}` } }
);
const disposition = response.headers.get('Content-Disposition');
const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? 'Ledger.xlsx';
const blob = await response.blob();
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url; a.download = filename; a.click();
URL.revokeObjectURL(url);
```

### Errors
- `404` — customer not found

---

## GET /api/v1/customers/{customerId}/ledger

Returns paginated ledger entries for the customer along with aggregate totals.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `entryType` | `string?` | Filter by `Debit` or `Credit` |
| `pageNumber` | `int?` | Defaults to `1` |
| `pageSize` | `int?` | Defaults to `20`, max `100` |

### Response `200 OK`

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "totalDebits": 850.00,
  "totalCredits": 600.00,
  "currentBalance": 250.00,
  "page": 1,
  "pageSize": 20,
  "totalCount": 5,
  "totalPages": 1,
  "entries": [
    {
      "id": "...",
      "entryType": "Debit",
      "amount": 120.00,
      "paymentMethod": null,
      "description": "Invoice INV-GAR00042 — Goods delivered (Trek TRK-00042)",
      "trekId": "a1b2c3d4-...",
      "trekNumber": "TRK-00042",
      "invoiceId": "b2c3d4e5-...",
      "invoiceNumber": "INV-GAR00042",
      "isAutoGenerated": true,
      "recordedAt": "2026-09-10T14:30:00Z",
      "recordedBy": "Kwame Asante"
    },
    {
      "id": "...",
      "entryType": "Credit",
      "amount": 300.00,
      "paymentMethod": "Cash",
      "description": "Invoice INV-GAR00042 — Payment received: Cash (Trek TRK-00042)",
      "trekId": "a1b2c3d4-...",
      "trekNumber": "TRK-00042",
      "invoiceId": "b2c3d4e5-...",
      "invoiceNumber": "INV-GAR00042",
      "isAutoGenerated": true,
      "recordedAt": "2026-09-10T14:30:00Z",
      "recordedBy": "Kwame Asante"
    },
    {
      "id": "...",
      "entryType": "Credit",
      "amount": 180.00,
      "paymentMethod": "MobileMoney",
      "description": "Invoice INV-GAR00042 — Payment received: MobileMoney (Trek TRK-00042)",
      "trekId": "a1b2c3d4-...",
      "trekNumber": "TRK-00042",
      "invoiceId": "b2c3d4e5-...",
      "invoiceNumber": "INV-GAR00042",
      "isAutoGenerated": true,
      "recordedAt": "2026-09-10T14:30:00Z",
      "recordedBy": "Kwame Asante"
    },
    {
      "id": "...",
      "entryType": "Debit",
      "amount": 250.00,
      "paymentMethod": null,
      "description": "Previous outstanding balance from last quarter",
      "trekId": null,
      "trekNumber": null,
      "invoiceId": null,
      "invoiceNumber": null,
      "isAutoGenerated": false,
      "recordedAt": "2026-08-15T09:00:00Z",
      "recordedBy": "Ama Owusu"
    }
  ]
}
```

### Field notes

| Field | Notes |
|---|---|
| `currentBalance` | `totalDebits − totalCredits`. Positive = customer owes money. |
| `paymentMethod` | `Cash`, `MobileMoney`, `Credit`, `Cheque`, or `BankTransfer`. Present on Credit entries, `null` on Debit (outstanding balance) and manual entries without a payment method. |
| `trekId` | GUID of the linked trek — use this to navigate to the trek detail page. `null` on manual entries. |
| `trekNumber` | Human-readable trek number for display. `null` on manual entries. |
| `invoiceId` | GUID of the sale invoice tied to the ledger entry's stop — resolved by joining `SaleInvoices` on `TrekkingTripStopId`. `null` on manual entries or when the stop never produced an invoice. |
| `invoiceNumber` | Human-readable invoice number (e.g. `INV-GAR00042`) for display and search. `null` under the same conditions as `invoiceId`. |
| `isAutoGenerated` | `true` = written by the invoice/payment or return-approval workflow. `false` = added manually by staff. |
| `recordedBy` | Full name of the staff member who created the entry (or the driver for auto entries). |

Entries are ordered newest first.

### Errors
- `404` — customer not found

---

## POST /api/v1/customers/{customerId}/ledger

Manually record a debit or credit against a customer. Use this for adjustments, opening balances, or payments not tied to an invoice. To settle an invoice, use its payment endpoint: a manual ledger credit does not update invoice paid/balance totals or issue a receipt.

### Request body

```json
{
  "entryType": "Credit",
  "amount": 200.00,
  "paymentMethod": "Cash",
  "description": "Cash payment collected at branch",
  "recordedAt": "2026-09-09T10:00:00Z"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `entryType` | `string` | Yes | `Debit` or `Credit` |
| `amount` | `number` | Yes | Must be greater than `0` |
| `paymentMethod` | `string?` | No | `Cash`, `MobileMoney`, `Credit`, `Cheque`, `BankTransfer` — recommended for Credit entries |
| `description` | `string` | Yes | Max 500 characters |
| `recordedAt` | `datetime?` | No | Defaults to current UTC time if omitted |

### Response `201 Created`

```json
{
  "id": "...",
  "entryType": "Credit",
  "amount": 200.00,
  "paymentMethod": "Cash",
  "description": "Cash payment collected at branch",
  "isAutoGenerated": false,
  "recordedAt": "2026-09-09T10:00:00Z",
  "recordedBy": "Ama Owusu"
}
```

### Errors
- `404` — customer not found
- `422` — validation failed (amount ≤ 0, missing description, invalid entryType)

---

## How auto-generation works

1. Scheduling issues every planned stop invoice; an active walk-in stop is issued immediately. A positive invoice total creates one linked **Debit** for the full amount billed.
2. Item/quantity/price changes update that debit while preserving existing payments.
3. Each successful invoice payment creates its own linked **Credit** and a separate payment receipt.
4. Trek completion leaves invoice debits, payment credits, manual adjustments and approved return credits intact.
5. Cancellation is restricted to unpaid, undelivered treks. It voids their invoices and removes invoice charges without clearing unrelated entries.

An invoice balance and the customer ledger balance serve different purposes:
manual adjustments and approved return credits affect the customer ledger without
changing invoice totals. Do not overwrite an invoice's balance from the customer
ledger. See [invoice payments](./27-invoice-first-payments.md).
