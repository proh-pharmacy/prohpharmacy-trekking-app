# 23 — Invoice Returns and Approval

Verified against the implementation on 2026-10-01. A return is captured at a stop on the current trek against a product on an existing invoice, potentially from an earlier trek. Each POST records one product; a stop can reference several invoices. New returns are `Pending`. Prices and refund amounts come from the invoice's stop-product prices.

## Routes and access

| Method | Route | Access | Success |
|---|---|---|---|
| POST | `/api/v1/treks/{trekId}/stops/{stopId}/returns` | Staff JWT | `201`, return response |
| POST | `/api/v1/treks/driver/{token}/stops/{stopId}/returns` | Driver token | `201`, return response |
| GET | `/api/v1/treks/{trekId}/returns?approvalStatus=Pending` | Staff JWT | `200`, return-list array |
| GET | `/api/v1/invoices/{invoiceNumber}/returns` | Staff JWT | `200`, same array |
| POST | `/api/v1/returns/{returnId}/approve` | Staff JWT | `200`, approval response; no request body |
| POST | `/api/v1/returns/{returnId}/reject` | Staff JWT | `200`, rejection response |

`approvalStatus` is optional and case-insensitive: `Pending`, `Approved`, `Rejected`. Lists are unpaginated, ordered by `recordedAt` ascending. A nonexistent trek currently gives `[]`; an unknown invoice gives HTTP `404`. POST handler failures use HTTP `422` even when the body code is `404` or `400`. The routes use plain `RequireAuthorization()` for staff; they do not enforce a dedicated admin role policy.

## Record a return

```json
{
  "saleInvoiceId": "<invoice-guid>",
  "productId": "<product-guid>",
  "basicQtyReturned": 4,
  "packagingQtyReturned": null,
  "refundMethod": "Cash",
  "reason": "Damaged stock",
  "gps": { "latitude": 5.6037, "longitude": -0.187, "accuracyMetres": 12.5 }
}
```

`saleInvoiceId` is the invoice's `id`, not its printable number. `basicQtyReturned` must be > 0; optional `packagingQtyReturned` must be > 0 when supplied (omit or use null for no packages). `reason` is optional, maximum 500 characters. `refundMethod` is optional: `Cash`, `MobileMoney`, `Credit`, `Cheque`, `BankTransfer`. `gps` is driver-only and optional, with latitude −90…90 and longitude −180…180.

Do not send `refundAmount`, unit prices, or `clientGeneratedId`. The new online endpoints do not provide return idempotency; prevent double submission and reconcile an uncertain result before retrying. Refund is `basicQtyReturned × basicUnitPrice + (packagingQtyReturned ?? 0) × (packagingUnitPrice ?? 0)`.

Response `201`:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "GAR-INV-00001",
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
  "recordedAt": "2026-10-01T11:00:00Z"
}
```

The recording trek cannot be `Completed` or `Cancelled`. The invoice and product line must exist. The driver handler also checks that the invoice's customer belongs to the trek region. It does not automatically start a scheduled trek.

## Review list

Both GET routes return this shape:

```json
[
  {
    "returnId": "<return-guid>",
    "stopId": "<current-stop-guid>",
    "customerName": "Accra Pharmacy Ltd",
    "invoiceNumber": "GAR-INV-00001",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "basicQtyReturned": 4,
    "packagingQtyReturned": null,
    "refundAmount": 10,
    "refundMethod": "Cash",
    "reason": "Damaged stock",
    "approvalStatus": "Pending",
    "rejectionReason": null,
    "recordedAt": "2026-10-01T11:00:00Z",
    "approvedAt": null
  }
]
```

`customerName` describes the capturing stop; the credit belongs to the invoice customer. `approvedAt` is populated for both approval and rejection. The existing embedded `stops[].returns` DTOs in trek/driver detail have not gained invoice or approval fields; use these dedicated staff lists for the review screen.

## Approve or reject

Approval takes no body and returns:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "GAR-INV-00001",
  "productName": "Paracetamol 500mg",
  "refundAmount": 10,
  "approvalStatus": "Approved",
  "approvedAt": "2026-10-01T16:00:00Z"
}
```

Rejection requires `{ "reason": "Goods do not match the invoice" }` (nonempty, maximum 500 characters) and returns:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "GAR-INV-00001",
  "productName": "Paracetamol 500mg",
  "approvalStatus": "Rejected",
  "rejectionReason": "Goods do not match the invoice",
  "rejectedAt": "2026-10-01T16:00:00Z"
}
```

Only `Pending` returns can be decided; repeated decisions return HTTP `422`. Approval creates an auto-generated customer ledger credit and adds returned quantities to the capturing trek vehicle's existing stock record. If no stock record exists, approval skips stock creation and its stock-ledger entry. Rejection has no ledger or stock effect. Neither decision updates invoice totals/status.

Completion through either staff status change or the driver completion endpoint notifies the trek creator of pending returns and sends an email when the creator can be resolved. Build the review workflow after completion; the approval/rejection handlers themselves do not enforce completed status.

After a decision, refresh return lists, customer ledger/balance, vehicle stock/ledger and the driver report. The trek PDF now displays approval status and mutes rejected rows.

## Integration boundaries

- Invoice lookup/detail/list routes require staff JWT. The token-only driver portal cannot yet search invoices using its token. Do not send the trek token as a Bearer JWT; a driver invoice lookup route is still needed.
- Return capture is online-only for the new workflow. The old offline `RecordReturn` branch still exists but does not set the required `saleInvoiceId`; it is incompatible with the new schema. Do not queue it.
- Neither recording handler checks that the invoice customer equals the current stop customer or caps cumulative returns at delivered quantities. Select invoices for the current customer and review quantities explicitly; backend validation is still needed for those guarantees.
- Existing DELETE return routes remain available (see [operations](./13-trek-operations.md)), but do not reverse approval credits/stock or check approval status. Restrict void UI to pending returns on open treks; use rejection for review decisions.
- Review after completion. Completion rebuilds auto-generated stop ledger entries, which can erase a return credit approved early. Repeating staff completion also deducts warehouse stock again; it is not a safe reconciliation action.

Sources: `Features/Trekking/RecordStopReturn.cs`, `RecordStopReturnByDriverToken.cs`, `ApproveReturn.cs`, `RejectReturn.cs`, `GetTrekReturns.cs`, `GetInvoiceReturns.cs`, `SyncOfflineActionsByDriverToken.cs`, `ChangeTrekStatus.cs`, `CompleteTrekByDriverToken.cs`.
