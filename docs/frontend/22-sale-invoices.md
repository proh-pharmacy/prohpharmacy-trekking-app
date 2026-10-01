# 22 — Sale Invoices

Verified against the implementation on 2026-10-01 (`8b5ca3d`, `aa7f3d8`). The code is authoritative where `docs/todo/phase2.md` differs.

## Lifecycle and UI

The admin and driver delivery-recording endpoints create or update one invoice per affected stop. Later submissions amend that invoice's totals; they do not issue a second invoice. Invoice lines are read from the stop products, not an immutable invoice-line snapshot. Only products with `deliveredAt` or `basicQtyDelivered` set are included.

Numbers are allocated by the server: `{SCOPE_CODE}-INV-{sequence:D5}`, for example `GAR-INV-00001`. The sequence belongs to the trek's branch when present, otherwise its region; the fallback code is `GH`. Never generate an official number locally.

Use an invoice list with customer, trek, status and date filters, an invoice detail/print view, and an invoice link on each recorded stop. The frontend renders invoice PDFs; there is no invoice PDF endpoint. Status values are `Issued`, `PartiallyPaid`, `Paid`, `Voided`; no void-invoice mutation currently exists.

## Endpoints

All routes below require a staff Bearer JWT, except the explicitly marked driver record route.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/invoices` | `200`, array or paginated invoice responses |
| GET | `/api/v1/invoices/{invoiceNumber}` | `200`, invoice response |
| GET | `/api/v1/treks/{trekId}/stops/{stopId}/invoice` | `200`, invoice response |
| POST | `/api/v1/treks/{id}/record` | `200`, delivery response including invoices |
| POST | `/api/v1/treks/driver/{token}/record` | `200`, same response; driver token only |

Single-invoice lookups return HTTP `404` when absent. The stop lookup is useful when a delivery has not yet produced an invoice. Record-handler failures use HTTP `422`, including missing treks; inspect the error body's `code` and `message` as well.

### List filters

| Query | Meaning |
|---|---|
| `customerId`, `trekId` | Optional GUID filters |
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
  "invoiceNumber": "GAR-INV-00001",
  "status": "PartiallyPaid",
  "issuedAt": "2026-10-01T10:30:00Z",
  "createdOffline": false,
  "trekkingTripId": "<trek-guid>",
  "trekNumber": "TRK-00001",
  "trekDate": "2026-10-01",
  "customerAccountId": "<customer-guid>",
  "customerName": "Accra Pharmacy Ltd",
  "totalAmount": 100,
  "totalPaid": 60,
  "balance": 40,
  "lineItems": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicQtyDelivered": 40,
      "packagingQtyDelivered": null,
      "basicUnitPrice": 2.5,
      "packagingUnitPrice": null,
      "lineTotal": 100,
      "amtPaid": 60,
      "balance": 40,
      "paymentMethod": "Cash",
      "isUnplanned": false
    }
  ]
}
```

`invoiceNumber` is nullable in the DTO. Both delivered quantities, `packagingUnitPrice`, `amtPaid`, `balance` and `paymentMethod` on lines are nullable. Invoice totals use actual delivered quantities × stop prices; invoice balance is total amount minus total paid. Status is `Paid` when balance ≤ 0, `PartiallyPaid` when paid > 0 and balance > 0, otherwise `Issued`. These may differ from stored per-product balances and planned `amountDue` shown elsewhere.

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
      "invoiceNumber": "GAR-INV-00001"
    }
  ]
}
```

Omit `stopInvoices` for ordinary online recording. For offline-origin invoices, retain one stable UUID per stop invoice across retries. On creation, a supplied UUID sets `createdOffline: true`; `recordedAt` supplies `issuedAt`. Matching prefers that UUID, then the stop ID. Existing invoices retain their number and issue time. Send at most one metadata entry per stop and never reuse an invoice UUID for another stop. Metadata alone does not create an invoice: include delivery products for the affected stop.

Persist the returned stop-to-invoice mapping, then refresh trek details and invoice queries. The driver's token does not authorize the staff invoice GET routes; driver invoice browsing needs a backend route before a standalone portal can support it.

The separate `/sync` action handler and direct unplanned-sale handlers do not call the invoice builder. After syncing deliveries or creating unplanned products, submit the affected product rows through `/record` with their server IDs and complete recorded values before completing the trek. Do not assume a successful sync has issued an invoice. See [offline integration](./18-offline-sync.md) and [returns](./23-invoice-returns.md).

Sources: `Features/Trekking/RecordTrekDelivery.cs`, `RecordTrekDeliveryByToken.cs`, `GetInvoice.cs`, `GetInvoiceList.cs`, `GetStopInvoice.cs`, `Utilities/QueryBuilder.cs`.
