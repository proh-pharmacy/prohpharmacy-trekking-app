# 22 — Sale Invoices

Verified against the implementation on 2026-10-01 (`8b5ca3d`, `aa7f3d8`). The code is authoritative where `docs/todo/phase2.md` differs.

## Lifecycle and UI

The admin and driver delivery-recording endpoints create or update one invoice per affected stop. Later submissions amend that invoice's totals; they do not issue a second invoice. Invoice lines are read from the stop products, not an immutable invoice-line snapshot. Only products with `deliveredAt` or `basicQtyDelivered` set are included.

Numbers are allocated by the server: `INV-{SCOPE_CODE}{sequence:D5}`, for example `INV-GAR00001`. The sequence belongs to the trek's branch when present, otherwise its region; the fallback code is `GH`. Never generate an official number locally.

Use an invoice list with customer, trek, status and date filters, an invoice detail/print view, and an invoice link on each recorded stop. The frontend renders invoice PDFs; there is no invoice PDF endpoint. Status values are `Issued`, `PartiallyPaid`, `Paid`, `Voided`; no void-invoice mutation currently exists.

## Endpoints

All routes below require a staff Bearer JWT, except the explicitly marked driver record route.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/invoices` | `200`, array or paginated invoice responses |
| GET | `/api/v1/invoices/{invoiceNumber}` | `200`, invoice response |
| GET | `/api/v1/invoices/export` | `200`, `.xlsx` or `.pdf` stream (see [Export](#export)) |
| GET | `/api/v1/treks/{trekId}/stops/{stopId}/invoice` | `200`, invoice response |
| POST | `/api/v1/treks/{id}/record` | `200`, delivery response including invoices |
| POST | `/api/v1/treks/driver/{token}/record` | `200`, same response; driver token only |

Single-invoice lookups return HTTP `404` when absent. The stop lookup is useful when a delivery has not yet produced an invoice. Record-handler failures use HTTP `422`, including missing treks; inspect the error body's `code` and `message` as well.

### List filters

| Query | Meaning |
|---|---|
| `customerId`, `trekId` | Optional GUID filters |
| `regionId` | Scopes to invoices whose **trek** ran in the given region (`TrekkingTrip.RegionId`). Walk-ins and cross-region deliveries follow trek region, not customer region. |
| `status` | Case-insensitive status filter |
| `dateFrom`, `dateTo` | Inclusive `issuedAt` bounds, ISO date-times |
| `search` | Invoice number search |
| `sort` | Entity field plus `_asc` / `_desc`, e.g. `issuedAt_desc` |
| `pageNumber`, `pageSize` | Send both, e.g. `1` and `20`, for the standard pagination envelope |

Without `pageSize`, the current query builder returns a plain array. With it, the envelope is `totalCount`, `totalPages`, `currentPage`, `pageSize`, `nextPageUrl`, `previousPageUrl`, `links`, `path`, `data` (see [conventions](./00-api-conventions.md)). There is no default page-size cap in this query builder.

### Invoice response

```json
{
  "id": "<invoice-guid>",
  "invoiceNumber": "INV-GAR00001",
  "status": "PartiallyPaid",
  "issuedAt": "2026-10-01T10:30:00Z",
  "createdOffline": false,
  "trekkingTripId": "<trek-guid>",
  "trekNumber": "TRK-00001",
  "trekDate": "2026-10-01",
  "driverName": "Kwame Asante",
  "salesStaffName": null,
  "vehicleDisplayName": "Delivery Van 1",
  "regionName": "Greater Accra Region",
  "customerAccountId": "<customer-guid>",
  "customerName": "Accra Pharmacy Ltd",
  "customerCode": "GAR00001",
  "customerTradingName": null,
  "customerPhone": "+233 24 000 0000",
  "customerWhatsAppNumber": null,
  "customerRegionName": "Greater Accra Region",
  "totalAmount": 100,
  "totalPaid": 60,
  "balance": 40,
  "lineItems": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tablet",
      "packagingUnitName": "Box",
      "basicQtyDelivered": 40,
      "packagingQtyDelivered": null,
      "basicUnitPrice": 2.5,
      "packagingUnitPrice": null,
      "lineTotal": 100,
      "amtPaid": 60,
      "balance": 40,
      "paymentMethod": "Cash",
      "isUnplanned": false,
      "deliveredAt": "2026-10-01T10:30:00Z"
    }
  ]
}
```

`invoiceNumber` is nullable in the DTO. Both delivered quantities, `packagingUnitPrice`, `amtPaid`, `balance`, `paymentMethod` and `deliveredAt` on lines are nullable. `basicUnitName` is always present; `packagingUnitName` is `null` for products with no packaging unit. The trek block (`driverName`, `salesStaffName`, `vehicleDisplayName`, `regionName`) and customer block (`customerCode`, `customerTradingName`, `customerPhone`, `customerWhatsAppNumber`, `customerRegionName`) are populated for both detail endpoints — enough to render a full invoice preview without extra lookups. Invoice totals use actual delivered quantities × stop prices; invoice balance is total amount minus total paid. Status is `Paid` when balance ≤ 0, `PartiallyPaid` when paid > 0 and balance > 0, otherwise `Issued`. These may differ from stored per-product balances and planned `amountDue` shown elsewhere.

Current limitation: `GetInvoiceList` uses this DTO but does not eagerly load `Stop.Products` / `Product`, unlike the two detail queries. Do not rely on list `lineItems` for printing or return selection; fetch the detail endpoint.

## Delivery request and response additions

Both record endpoints accept the existing `products` plus optional `stopInvoices`. IDs in `stopInvoices` refer to stops, not stop-product rows.

```json
{
  "products": [
    {
      "stopProductId": "<stop-product-guid>",
      "basicQtyDelivered": 40,
      "packagingQtyDelivered": null,
      "paymentMethod": "Cash",
      "amtPaid": 60,
      "balance": 40,
      "notes": null
    }
  ],
  "stopInvoices": [
    {
      "stopId": "<stop-guid>",
      "clientGeneratedId": "<stable-local-invoice-guid>",
      "recordedAt": "2026-10-01T10:30:00Z"
    }
  ]
}
```

```json
{
  "trekId": "<trek-guid>",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1,
  "invoices": [
    {
      "stopId": "<stop-guid>",
      "invoiceId": "<invoice-guid>",
      "invoiceNumber": "INV-GAR00001"
    }
  ]
}
```

Omit `stopInvoices` for ordinary online recording. For offline-origin invoices, retain one stable UUID per stop invoice across retries. On creation, a supplied UUID sets `createdOffline: true`; `recordedAt` supplies `issuedAt`. Matching prefers that UUID, then the stop ID. Existing invoices retain their number and issue time. Send at most one metadata entry per stop and never reuse an invoice UUID for another stop. Metadata alone does not create an invoice: include delivery products for the affected stop.

Persist the returned stop-to-invoice mapping, then refresh trek details and invoice queries. The driver's token does not authorize the staff invoice GET routes; driver invoice browsing needs a backend route before a standalone portal can support it.

The separate `/sync` action handler and direct unplanned-sale handlers do not call the invoice builder. After syncing deliveries or creating unplanned products, submit the affected product rows through `/record` with their server IDs and complete recorded values before completing the trek. Do not assume a successful sync has issued an invoice. See [offline integration](./18-offline-sync.md) and [returns](./23-invoice-returns.md).

Sources: `Features/Trekking/RecordTrekDelivery.cs`, `RecordTrekDeliveryByToken.cs`, `GetInvoice.cs`, `GetInvoiceList.cs`, `GetStopInvoice.cs`, `Utilities/QueryBuilder.cs`.

## Export

```
GET /api/v1/invoices/export?format=excel|pdf
```

Staff Bearer JWT. Streams **all** invoices matching the filters — pagination is intentionally ignored so the export reflects the full result set the current filters describe. The file is generated on-demand and not persisted server-side; immutability is provided by streaming a self-describing snapshot (filter line + generation timestamp + row count are embedded in the file header).

### Query parameters

The filter pipeline is identical to [`GET /api/v1/invoices`](#list-filters):

| Query | Meaning |
|---|---|
| `format` | `excel` (default) or `pdf` |
| `customerId` | Scope to one customer's invoices |
| `trekId` | Scope to one trek's invoices |
| `regionId` | Scope to invoices from treks that ran in a region (`TrekkingTrip.RegionId`) |
| `status` | Case-insensitive — `Issued`, `PartiallyPaid`, `Paid`, `Voided` |
| `dateFrom`, `dateTo` | Inclusive `issuedAt` bounds, ISO date-times |
| `search` | Invoice-number substring (case-insensitive) |
| `sort` | Entity field + `_asc` / `_desc`; default `issuedAt_desc` |

Omitting `format` defaults to Excel. An invalid value returns `422` with `code=BadRequest`.

### Response

Both formats stream with `Content-Disposition: attachment; filename="Invoices-YYYYMMDDHHmm.<ext>"`:

| `format` | Content-Type | Extension |
|---|---|---|
| `excel` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | `.xlsx` |
| `pdf` | `application/pdf` | `.pdf` |

### File contents

Same columns in both formats, same order:

`INVOICE NO.` · `ISSUED AT` · `STATUS` · `TREK NO.` · `TREK DATE` · `CUSTOMER CODE` · `CUSTOMER NAME` · `REGION` · `TOTAL AMOUNT (GHS)` · `TOTAL PAID (GHS)` · `BALANCE (GHS)`

A header band at the top of each file states the applied filters (Customer, Trek, Status, Period, Search, Sort), the generation timestamp (`UTC`), and the total row count. A `TOTAL` row at the bottom sums `TOTAL AMOUNT`, `TOTAL PAID`, and `BALANCE` across every row returned. Lines are **invoices** (one row per invoice) — line-item breakdowns are not included; link to the invoice detail endpoint for per-line data.

### Frontend usage

```ts
const url = new URL("/api/v1/invoices/export", API_BASE);
url.searchParams.set("format", "excel"); // or "pdf"
if (customerId) url.searchParams.set("customerId", customerId);
if (dateFrom)   url.searchParams.set("dateFrom", dateFrom.toISOString());
// ...other filters identical to the list endpoint

const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
const blob = await res.blob();
const filename = /filename="(.+)"/.exec(res.headers.get("content-disposition") ?? "")?.[1]
              ?? `Invoices.${format === "pdf" ? "pdf" : "xlsx"}`;
// save blob as `filename`
```

Mirror the exact filter state currently applied to the list view before firing the export so the saved file matches what the user is looking at. The embedded filter line in the file makes mismatches obvious if they occur.

Sources: `Features/Trekking/ExportInvoiceList.cs`, `Services/Pdf/InvoiceListPdfGenerator.cs`.

## Client-side invoice generation (QR / offline)

For driver-side client-generated invoice PDFs (where a QR scan at the stop must resolve to the delivered line items without regenerating an invoice number):

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/treks/driver/{token}/stops/by-customer?customerCode=…` | `200`, delivery payload below |
| GET | `/api/v1/treks/driver/{token}/stops/by-customer?clientGeneratedId=…` | `200`, delivery payload below |

Anonymous (driver token). Provide **exactly one** of `customerCode` or `clientGeneratedId` as a query string parameter:

- `customerCode` — exact, case-insensitive match on `CustomerAccount.CustomerCode`. Use for customers already synced to the device.
- `clientGeneratedId` — matches `CustomerAccount.ClientGeneratedId` (the device-generated UUID submitted with `RegisterCustomer`). Use for customers created offline whose server-assigned `customerCode` has not yet been reconciled into the local store.

Status codes:
- `200` — stop found, delivery payload returned
- `404` — invalid token, or no stop on this trek for the given identifier
- `422` — neither identifier was provided, or both were provided simultaneously

```json
{
  "trekNumber": "TRK-00001",
  "scheduledDate": "2026-10-02",
  "trekStatus": "InProgress",
  "regionName": "Greater Accra Region",
  "driverName": "Kwame Asante",
  "salesStaffName": null,
  "vehicleDisplayName": "Delivery Van 1",
  "stop": {
    "stopId": "<stop-guid>",
    "sequence": 1,
    "isWalkIn": false,
    "customer": {
      "id": "<customer-guid>",
      "customerCode": "GAR00001",
      "clientGeneratedId": null,
      "businessName": "Accra Pharmacy Ltd",
      "tradingName": null,
      "primaryPhoneNumber": "+233 24 000 0000",
      "whatsAppNumber": null,
      "regionName": "Greater Accra Region"
    },
    "invoice": {
      "id": "<invoice-guid>",
      "invoiceNumber": "INV-GAR00001",
      "status": "PartiallyPaid",
      "issuedAt": "2026-10-02T10:30:00Z"
    },
    "products": [
      {
        "productId": "<product-guid>",
        "productName": "Paracetamol 500mg",
        "basicUnitName": "Tablet",
        "packagingUnitName": "Box",
        "basicQtyDelivered": 8,
        "packagingQtyDelivered": 0,
        "basicUnitPrice": 1.50,
        "packagingUnitPrice": 15.00,
        "lineTotal": 12.00,
        "amtPaid": 10.00,
        "balance": 2.00,
        "paymentMethod": "Cash",
        "deliveredAt": "2026-10-02T10:30:00Z"
      }
    ],
    "totals": { "amountDue": 12.00, "amtPaid": 10.00, "balance": 2.00 }
  }
}
```

Only **delivered** product lines are returned (`deliveredAt` or `basicQtyDelivered` set) — planned-but-undelivered rows and returns are excluded. `stop.invoice` is `null` until the first delivery has been recorded for the stop (which is also when the server allocates the invoice number). `totals` are summed from the returned lines; the frontend does not need to recompute.

`customer.clientGeneratedId` echoes back the UUID originally submitted via `RegisterCustomer` (null for customers created from the admin panel). Use it alongside `customerCode` as a stable local key when reconciling offline-registered customers.

Use this when the driver's QR links to a customer anchor and the client needs to render the invoice without round-tripping through the staff invoice endpoints (which require a Bearer JWT).
