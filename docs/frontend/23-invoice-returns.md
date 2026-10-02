# 23 — Invoice Returns and Approval

Verified against the implementation on 2026-10-02. Returns are **customer-first**: the driver picks any customer in their region, picks one of that customer's invoices (any trek), picks one or more products with quantities, and submits a batch. All returns in a batch attach to the customer's stop on the current trek — the stop is auto-added as a walk-in if the customer is not already on the trek. New returns are `Pending`. Prices and refund amounts come from the invoice's stop-product prices.

## Routes and access

| Method | Route | Access | Success |
|---|---|---|---|
| GET | `/api/v1/treks/driver/{token}/customers/{customerId}/invoices` | Driver token | `200`, invoice-list array |
| POST | `/api/v1/treks/driver/{token}/returns` | Driver token | `201`, batch return response |
| POST | `/api/v1/treks/{trekId}/stops/{stopId}/returns` | Staff JWT | `201`, single return response |
| GET | `/api/v1/treks/{trekId}/returns?approvalStatus=Pending` | Staff JWT | `200`, return-list array |
| GET | `/api/v1/invoices/{invoiceNumber}/returns` | Staff JWT | `200`, same array |
| POST | `/api/v1/returns/{returnId}/approve` | Staff JWT | `200`, approval response; no request body |
| POST | `/api/v1/returns/{returnId}/reject` | Staff JWT | `200`, rejection response |

`approvalStatus` is optional and case-insensitive: `Pending`, `Approved`, `Rejected`. Review lists are unpaginated, ordered by `recordedAt` ascending. POST handler failures use HTTP `422` even when the body code is `404` or `400`. The routes use plain `RequireAuthorization()` for staff; they do not enforce a dedicated admin role policy.

## Driver flow

### 1. List the customer's invoices

```
GET /api/v1/treks/driver/{token}/customers/{customerId}/invoices
    ?from=2026-01-01
    &to=2026-10-02
    &invoiceNumber=INV-GAR
```

All query parameters are optional. `from` / `to` are `YYYY-MM-DD` dates applied to `issuedAt` (inclusive). `invoiceNumber` is a case-insensitive substring. The customer is picked from the regional customer list the driver portal already caches. Response (newest first):

```json
[
  {
    "id": "<invoice-guid>",
    "invoiceNumber": "INV-GAR00001",
    "issuedAt": "2026-08-15T10:00:00Z",
    "trekkingTripId": "<trek-guid>",
    "trekkingTripStopId": "<original-stop-guid>",
    "totalAmount": 100,
    "totalPaid": 60,
    "balance": 40,
    "lineItems": [
      {
        "productId": "<product-guid>",
        "productName": "Paracetamol 500mg",
        "basicUnitName": "Tablet",
        "packagingUnitName": "Box",
        "basicQtyDelivered": 20,
        "packagingQtyDelivered": 2,
        "basicUnitPrice": 2.5,
        "packagingUnitPrice": 50
      }
    ]
  }
]
```

Only delivered line items are included (products on the invoice without any delivered quantity are omitted — they cannot be returned).

### 2. Submit the batch

```
POST /api/v1/treks/driver/{token}/returns
```

```json
{
  "customerAccountId": "<customer-guid>",
  "saleInvoiceId": "<invoice-guid>",
  "items": [
    {
      "productId": "<product-guid>",
      "basicQtyReturned": 4,
      "packagingQtyReturned": null,
      "refundMethod": "Cash",
      "reason": "Damaged stock"
    },
    {
      "productId": "<another-product-guid>",
      "basicQtyReturned": 2,
      "packagingQtyReturned": 1,
      "refundMethod": "Cash"
    }
  ],
  "gps": { "latitude": 5.6037, "longitude": -0.187, "accuracyMetres": 12.5 }
}
```

`items` must be non-empty. Each item requires `productId` and `basicQtyReturned > 0`. `packagingQtyReturned`, `refundMethod`, and `reason` are per-item (so different products in the same batch can have different refund methods). `reason` is max 500 characters. `refundMethod` values: `Cash`, `MobileMoney`, `Credit`, `Cheque`, `BankTransfer`. `gps` is optional, applies to all items in the batch, latitude −90…90 and longitude −180…180.

Do not send `refundAmount`, unit prices, or `clientGeneratedId`. The endpoint is not idempotent — prevent double submission and reconcile uncertain results before retrying. Refund per item is `basicQtyReturned × basicUnitPrice + (packagingQtyReturned ?? 0) × (packagingUnitPrice ?? 0)`.

Response `201`:

```json
{
  "stopId": "<stop-guid>",
  "stopWasAutoAdded": true,
  "returns": [
    {
      "returnId": "<return-guid>",
      "invoiceNumber": "INV-GAR00001",
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicQtyReturned": 4,
      "packagingQtyReturned": null,
      "basicUnitPrice": 2.5,
      "packagingUnitPrice": null,
      "refundAmount": 10,
      "refundMethod": "Cash",
      "reason": "Damaged stock",
      "approvalStatus": "Pending",
      "recordedAt": "2026-10-02T11:00:00Z"
    }
  ]
}
```

`stopWasAutoAdded` is `true` when a walk-in stop was created on the fly (customer not already on the current trek). After a `true` response, refresh the trek detail so the new stop appears. If the trek was `Scheduled`, submitting an auto-added-stop batch also flips it to `InProgress`.

### Failure modes

| HTTP | Body code | When |
|---|---|---|
| `404` | `404` | token invalid, invoice not found |
| `422` | `400` | trek is `Completed`/`Cancelled`, invoice customer ≠ `customerAccountId`, a product in `items` is not on the invoice |
| `422` | `422` | validation (empty items, non-positive quantities, oversized `reason`) |

The batch is atomic — if any item fails, nothing is saved.

## Review list

Both staff GET routes return this shape:

```json
[
  {
    "returnId": "<return-guid>",
    "stopId": "<capturing-stop-guid>",
    "customerName": "Accra Pharmacy Ltd",
    "invoiceNumber": "INV-GAR00001",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "basicQtyReturned": 4,
    "packagingQtyReturned": null,
    "refundAmount": 10,
    "refundMethod": "Cash",
    "reason": "Damaged stock",
    "approvalStatus": "Pending",
    "rejectionReason": null,
    "recordedAt": "2026-10-02T11:00:00Z",
    "approvedAt": null
  }
]
```

`customerName` describes the capturing stop (which equals the invoice customer, since the batch endpoint enforces the match). `approvedAt` is populated for both approval and rejection. The embedded `stops[].returns` DTOs in trek/driver detail do not include invoice or approval fields; use these dedicated staff lists for the review screen.

## Approve or reject

Approval takes no body and returns:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "INV-GAR00001",
  "productName": "Paracetamol 500mg",
  "refundAmount": 10,
  "approvalStatus": "Approved",
  "approvedAt": "2026-10-02T16:00:00Z"
}
```

Rejection requires `{ "reason": "Goods do not match the invoice" }` (nonempty, maximum 500 characters) and returns:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "INV-GAR00001",
  "productName": "Paracetamol 500mg",
  "approvalStatus": "Rejected",
  "rejectionReason": "Goods do not match the invoice",
  "rejectedAt": "2026-10-02T16:00:00Z"
}
```

Only `Pending` returns can be decided; repeated decisions return HTTP `422`. Approval creates an auto-generated customer ledger credit and adds returned quantities to the capturing trek vehicle's existing stock record. If no stock record exists, approval skips stock creation and its stock-ledger entry. Rejection has no ledger or stock effect. Neither decision updates invoice totals/status.

Completion through either staff status change or the driver completion endpoint notifies the trek creator of pending returns and sends an email when the creator can be resolved. Build the review workflow after completion; the approval/rejection handlers themselves do not enforce completed status.

After a decision, refresh return lists, customer ledger/balance, vehicle stock/ledger and the driver report. The trek PDF displays approval status and mutes rejected rows.

## Integration boundaries

- Invoice lookup/detail/list routes still require staff JWT. The driver portal lists the stop customer's invoice history via `GET /treks/driver/{token}/customers/{customerId}/invoices`. General driver-token invoice browsing (across customers) is still not exposed.
- Return capture is online-only. The legacy offline `RecordReturn` handler is incompatible with the new schema — do not queue it.
- Walk-in stops auto-added by the batch endpoint have no products, zero deliveries, and only returns. They appear in the trek detail with `isWalkIn: true` and empty `products: []`.
- Neither recording handler caps cumulative returns at delivered quantities — review quantities explicitly in the UI.
- Existing DELETE return routes remain available (see [operations](./13-trek-operations.md)), but do not reverse approval credits/stock or check approval status. Restrict void UI to pending returns on open treks; use rejection for review decisions.
- Review after completion. Completion rebuilds auto-generated stop ledger entries, which can erase a return credit approved early. Repeating staff completion also deducts warehouse stock again; it is not a safe reconciliation action.
- The staff route `POST /api/v1/treks/{trekId}/stops/{stopId}/returns` still records a single return against a specific stop + invoice. The driver portal no longer uses the stop-scoped flow — the batch customer-focused route is the only driver return endpoint.

Sources: `Features/Trekking/RecordCustomerReturnsByDriverToken.cs`, `GetCustomerInvoicesByDriverToken.cs`, `RecordStopReturn.cs`, `ApproveReturn.cs`, `RejectReturn.cs`, `GetTrekReturns.cs`, `GetInvoiceReturns.cs`, `ChangeTrekStatus.cs`, `CompleteTrekByDriverToken.cs`.
