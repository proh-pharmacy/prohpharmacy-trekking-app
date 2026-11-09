# 22 — Sale Invoices

> Updated for the invoice-first backend on 2026-11-08. See [Invoice First & Payments](./27-invoice-first-payments.md) for the payment and receipt contracts.


Invoice and payment contracts checked against the current backend source on 2026-11-08. This documents the implementation; staging verification is still pending.

## Lifecycle and UI

Adding a customer stop to a Draft trek creates planning data with nullable invoice fields. Changing the trek to Scheduled issues one numbered invoice per stop with its planned products and zero paid amount. A stop added to a Scheduled or InProgress trek is invoiced immediately. Driver deliveries, extra products and price changes update that same invoice. Collecting money is a separate payment request that issues a receipt. Invoice lines are read from the stop products; they are not immutable snapshots. Planned and delivered lines are included.

Numbers are allocated by the server: `INV-{SCOPE_CODE}{sequence:D5}`, for example `INV-GAR00001`. The sequence belongs to the trek's branch when present, otherwise its region; the fallback code is `GH`. Never generate an official number locally.

Use an invoice list with customer, trek, status and date filters, an invoice detail/print view, and an invoice link on each planned or walk-in stop. The frontend renders invoice PDFs; there is no invoice PDF endpoint. Status values are `Issued`, `PartiallyPaid`, `Paid`, `Voided`; no void-invoice mutation currently exists.

## Endpoints

All routes below require a staff Bearer JWT, except the explicitly marked driver-token routes.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/invoices` | `200`, array or paginated invoice responses |
| GET | `/api/v1/invoices/{invoiceNumber}` | `200`, invoice response |
| GET | `/api/v1/invoices/export` | `200`, `.xlsx` or `.pdf` stream (see [Export](#export)) |
| GET | `/api/v1/treks/{trekId}/stops/{stopId}/invoice` | `200`, invoice response |
| GET | `/api/v1/treks/driver/{token}/invoices/{invoiceId}` | `200`, invoice response; driver token only |
| POST | `/api/v1/invoices/{invoiceId}/payments` | `200`, payment receipt; staff JWT |
| POST | `/api/v1/treks/driver/{token}/invoices/{invoiceId}/payments` | `200`, payment receipt; driver token only |
| GET | `/api/v1/payments/{paymentId}/receipt` | `200`, receipt; staff JWT |
| GET | `/api/v1/treks/driver/{token}/payments/{paymentId}/receipt` | `200`, receipt; driver token only |
| POST | `/api/v1/treks/{id}/record` | `200`, delivery response including invoices |
| POST | `/api/v1/treks/driver/{token}/record` | `200`, same response; driver token only |

Single-invoice lookups return HTTP `404` when absent. The stop lookup opens an invoice before delivery or payment. Record-handler failures use HTTP `422`, including missing treks; inspect the error body's `code` and `message` as well.

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
  "status": "Issued",
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
  "totalPaid": 0,
  "balance": 100,
  "payments": [],
  "lineItems": [
    {
      "stopProductId": "<stop-product-guid>",
      "basicQuantity": 40,
      "packagingQuantity": 0,
      "plannedBasicQuantity": 40,
      "plannedPackagingQuantity": null,
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tablet",
      "packagingUnitName": "Box",
      "basicQtyDelivered": 40,
      "packagingQtyDelivered": null,
      "basicUnitPrice": 2.5,
      "packagingUnitPrice": null,
      "lineTotal": 100,
      "amtPaid": null,
      "balance": null,
      "paymentMethod": null,
      "isUnplanned": false,
      "deliveredAt": "2026-10-01T10:30:00Z"
    }
  ]
}
```

`invoiceNumber` is nullable in the DTO. Both delivered quantities, `packagingUnitPrice`, `amtPaid`, `balance`, `paymentMethod` and `deliveredAt` on lines are nullable. `basicUnitName` is always present; `packagingUnitName` is `null` for products with no packaging unit. The trek block (`driverName`, `salesStaffName`, `vehicleDisplayName`, `regionName`) and customer block (`customerCode`, `customerTradingName`, `customerPhone`, `customerWhatsAppNumber`, `customerRegionName`) are populated for both detail endpoints — enough to render a full invoice preview without extra lookups. Invoice quantities use planned quantities until delivery is recorded for the line, then actual delivered quantities. Use `basicQuantity`, `packagingQuantity` and `lineTotal` for invoice display. Use invoice-level `totalAmount`, `totalPaid`, `balance` and `status`; line `amtPaid`, `balance` and `paymentMethod` are retained legacy data and do not describe new collections. `payments` contains receipt objects as documented in [guide 27](./27-invoice-first-payments.md#record-a-payment). Empty invoices remain `Issued`; positive fully paid invoices are `Paid`.


Current limitation: `GetInvoiceList` uses this DTO but does not eagerly load `Stop.Products` / `Product`, unlike the two detail queries. It also does not load `Payments`. Do not rely on list `lineItems` or `payments` for printing, payment history or return selection; fetch the detail endpoint.

## Delivery and payment submission

Both record routes accept quantities and notes. Omit `amtPaid`, `paymentMethod`,
and `balance`; any non-null value, including zero, is rejected. Omit the deprecated
`stopInvoices` input. Planned stops receive invoice identity during scheduling; active
walk-in stops receive it during creation.

```json
{
  "products": [
    {
      "stopProductId": "<stop-product-guid>",
      "basicQtyDelivered": 40,
      "packagingQtyDelivered": 0,
      "notes": null
    }
  ]
}
```

The response retains its existing shape:

```json
{
  "trekId": "<trek-guid>",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1,
  "invoices": [
    { "stopId": "<stop-guid>", "invoiceId": "<invoice-guid>", "invoiceNumber": "INV-GAR00001" }
  ]
}
```

Persist the mapping and refresh the invoice. Sync and unplanned-product additions
also refresh the invoice automatically; do not make an extra `/record` call just
to create an invoice. Driver invoice reads use the token route above.

For **Collect payment**, use the separate payment POST with the invoice GUID,
`clientGeneratedId`, `amount`, and `paymentMethod`. Render its returned receipt and
refresh invoice totals. The exact request, receipt response, retry behavior and
offline action are in [Invoice First & Payments](./27-invoice-first-payments.md).
The invoice remains available to view/print before payment; print the receipt only
after a successful payment response. The frontend renders both documents.

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
        "stopProductId": "<stop-product-guid>",
        "basicQuantity": 8,
        "packagingQuantity": 0,
        "plannedBasicQuantity": 8,
        "plannedPackagingQuantity": null,
        "productId": "<product-guid>",
        "productName": "Paracetamol 500mg",
        "basicUnitName": "Tablet",
        "packagingUnitName": "Box",
        "basicQtyDelivered": 8,
        "packagingQtyDelivered": 0,
        "basicUnitPrice": 1.50,
        "packagingUnitPrice": 15.00,
        "lineTotal": 12.00,
        "amtPaid": null,
        "balance": null,
        "paymentMethod": null,
        "deliveredAt": "2026-10-02T10:30:00Z"
      }
    ],
    "totals": { "amountDue": 12.00, "amtPaid": 10.00, "balance": 2.00 }
  }
}
```

Planned and delivered product lines are returned. Each line also includes `stopProductId`, `basicQuantity`, `packagingQuantity`, `plannedBasicQuantity`, and `plannedPackagingQuantity`. Planned invoices are created when the trek is scheduled; nullable invoice fields are expected while it is Draft. `totals` comes from the invoice, not legacy per-line payments. Fetch the driver invoice detail for its `payments` history.

`customer.clientGeneratedId` echoes back the UUID originally submitted via `RegisterCustomer` (null for customers created from the admin panel). Use it alongside `customerCode` as a stable local key when reconciling offline-registered customers.

Use this when the driver's QR links to a customer anchor and the client needs to render the invoice without round-tripping through the staff invoice endpoints (which require a Bearer JWT).
