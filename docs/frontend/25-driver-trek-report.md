# 25 — Driver Trek Financial Report

Verified against `Features/Trekking/GetDriverTrekReport.cs` and `Services/Pdf/DriverReportPdfGenerator.cs` on 2026-10-01 (`aa7f3d8`). Add a **Report** tab and **Download PDF** action to the driver portal. Both routes use the driver token, need no JWT and work at any trek status, including completed/cancelled treks.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/treks/driver/{token}/report` | `200`, JSON below |
| GET | `/api/v1/treks/driver/{token}/report/pdf` | `200`, `application/pdf` attachment |

Invalid tokens return HTTP `404` with `{ "code": "404", "message": "Trek not found. The token may be invalid." }`. There is no `/api/v1/treks/{trekId}/driver-report` route, despite that route appearing in the phase notes.

## JSON response

```json
{
  "generatedAt": "2026-10-01T16:00:00Z",
  "trekNumber": "TRK-00001",
  "scheduledDate": "2026-10-01",
  "status": "Completed",
  "driverName": "Kwame Asante",
  "salesStaffName": null,
  "vehicleDisplayName": "Delivery Van 1",
  "regionName": "Greater Accra Region",
  "summary": {
    "totalSalesValue": 100,
    "totalCollected": 60,
    "totalOutstanding": 40,
    "totalApprovedRefunds": 10,
    "totalPendingRefunds": 15,
    "totalRejectedRefunds": 0,
    "approvedRefundCount": 1,
    "pendingRefundCount": 2,
    "rejectedRefundCount": 0,
    "netCashOnHand": 50,
    "trekNetSales": 90,
    "cashCollected": 40,
    "mobileMoneyCollected": 20,
    "cashRefundsPaidOut": 25,
    "mobileMoneyRefundsPaidOut": 0,
    "physicalCashInHand": 15,
    "mobileMoneyBalance": 20,
    "totalStops": 1,
    "stopsVisited": 1
  },
  "collectionsByMethod": [
    { "method": "Cash", "amount": 60 }
  ],
  "stops": [
    {
      "sequence": 1,
      "customerName": "Accra Pharmacy Ltd",
      "invoiceId": "<invoice-guid>",
      "invoiceNumber": "INV-GAR00001",
      "amountDue": 100,
      "amtPaid": 60,
      "balance": 40,
      "paymentMethods": ["Cash", "MobileMoney"],
      "returns": [
        {
          "productName": "Paracetamol 500mg",
          "basicQtyReturned": 4,
          "refundAmount": 10,
          "refundMethod": "Cash",
          "approvalStatus": "Approved"
        }
      ]
    }
  ],
  "stockSummary": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicQtyLoaded": 100,
      "basicQtyDelivered": 40,
      "basicQtyApprovedReturns": 4,
      "basicQtyRemaining": 64
    }
  ],
  "refunds": [
    {
      "returnId": "<return-guid>",
      "sequence": 1,
      "customerName": "Accra Pharmacy Ltd",
      "invoiceId": "<invoice-guid>",
      "invoiceNumber": "INV-GAR00001",
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicQtyReturned": 4,
      "packagingQtyReturned": null,
      "refundAmount": 10,
      "refundMethod": "Cash",
      "approvalStatus": "Approved",
      "reason": "Damaged stock",
      "rejectionReason": null,
      "recordedAt": "2026-10-02T11:00:00Z",
      "approvedAt": "2026-10-02T16:00:00Z"
    },
    {
      "returnId": "<return-guid>",
      "sequence": 2,
      "customerName": "Spintex Pharmacy",
      "invoiceId": "<invoice-guid>",
      "invoiceNumber": "INV-GAR00007",
      "productId": "<product-guid>",
      "productName": "Novamol 500mg Supp",
      "basicQtyReturned": 3,
      "packagingQtyReturned": null,
      "refundAmount": 15,
      "refundMethod": "Cash",
      "approvalStatus": "Pending",
      "reason": "Short-dated",
      "rejectionReason": null,
      "recordedAt": "2026-10-02T13:40:00Z",
      "approvedAt": null
    }
  ]
}
```

`salesStaffName`, stop `invoiceId`, stop `invoiceNumber`, and return `refundAmount` / `refundMethod` are nullable (invoice fields are null until a delivery has been recorded on the stop). `invoiceId` is the stop's invoice GUID — use it as `saleInvoiceId` when recording returns on the driver route. `paymentMethods` is always an array (empty when no payment has been received on the stop yet). Arrays may be empty. This endpoint has no pagination or filters.

## Meaning of the figures

| Field | Current calculation |
|---|---|
| `totalSalesValue` | Sum of `amountDue` across stop products that have been delivered (`deliveredAt` set or `basicQtyDelivered` recorded). Planned-but-undelivered products are excluded. Matches the sum of all invoice totals on the trek. |
| `totalCollected` | Sum of stop-product `amtPaid` |
| `totalOutstanding` | Sum of stored stop-product `balance` |
| `totalApprovedRefunds` | Sum of refund amounts on approved returns captured on this trek |
| `totalPendingRefunds` | Sum of refund amounts on **pending** returns — not yet reflected in the customer ledger or vehicle stock |
| `totalRejectedRefunds` | Sum of refund amounts on rejected returns (no financial effect; shown for completeness) |
| `approvedRefundCount` / `pendingRefundCount` / `rejectedRefundCount` | Counts matching the three totals above |
| `netCashOnHand` | Cash collected + MobileMoney collected − approved **Cash** refunds. **Kept for back-compat** — prefer the explicit money-position fields below. |
| `trekNetSales` | `totalSalesValue − totalApprovedRefunds` — the trek's business P&L (approved refunds reduce earnings; pending/rejected don't). |
| `cashCollected` / `mobileMoneyCollected` | `totalCollected` split by payment method, from product `AmtPaid`. |
| `cashRefundsPaidOut` | Sum of **approved + pending** Cash refunds. Assumption: cash is physically handed back to the customer at the stop when the driver captures the return, so pending Cash refunds have already left the pocket. Rejected refunds are excluded (treated as informational). |
| `mobileMoneyRefundsPaidOut` | Same logic for MobileMoney. Typically 0 — MoMo reversals are usually processed by admin, not by the driver at a stop. |
| `physicalCashInHand` | `cashCollected − cashRefundsPaidOut` — the cash the driver should physically have. |
| `mobileMoneyBalance` | `mobileMoneyCollected − mobileMoneyRefundsPaidOut`. |
| `stopsVisited` | Stops with any product having basic delivered quantity > 0 or amount paid > 0 |
| `basicQtyRemaining` | `max(0, loaded − delivered + approved returns)` per trek-load product |

The report exposes three distinct money views — surface them separately on the UI:

1. **Trek P&L** (what the trek earned): `totalSalesValue`, `totalApprovedRefunds` (deduction), `trekNetSales`.
2. **Money Position** (what's where right now): `cashCollected`, `cashRefundsPaidOut`, `physicalCashInHand`, `mobileMoneyBalance`.
3. **Deferred / Exposure**: `totalOutstanding` (customer debt), `totalPendingRefunds` + `pendingRefundCount` (awaiting admin approval), `totalRejectedRefunds` + `rejectedRefundCount` (informational).

`netCashOnHand` is retained for backwards compatibility but has an inconsistent meaning (it mixes Cash + MoMo and subtracts only approved Cash refunds). Prefer `physicalCashInHand` + `mobileMoneyBalance`. Collections come from product records, so independent customer ledger payments are not included.

`collectionsByMethod` contains only methods with positive paid entries, sorted by amount descending; do not expect zero-valued rows for every method. Stops are ordered by sequence. Each stop's `paymentMethods` is a de-duplicated, alphabetically-sorted list of methods actually used to pay on that stop (products with `amtPaid > 0`); render it as a chip row (e.g. `Cash · MobileMoney`) so mixed-payment stops read correctly. Return lists show all approval states, but totals and stock reconciliation use approved returns only.

Each stop's `amountDue`, like the trek-level `totalSalesValue`, only counts products that have been delivered — a stop with planned-but-undelivered products shows `amountDue = 0` until the first delivery is recorded, and `amountDue − amtPaid === balance` always holds.

Stock reconciliation covers **only products with trek stock-load rows**, using basic units only. It omits unallocated products and packaging quantities. It is a trek allocation calculation, not the live vehicle warehouse balance. Stop rows do not include sold-product line items or IDs; use the existing trek detail data if the UI needs those details. The phase notes' richer per-product stop breakdown is not in this DTO.

## Refunds table

The top-level `refunds[]` is a denormalised, flat list of every return captured on this trek — one row per return across all stops, ordered by `recordedAt` ascending. Use it directly as the data source for the driver-side refunds table; the per-stop `stops[].returns[]` is still returned for backwards compatibility but is a lighter view (no stop sequence, invoice fields, timestamps or GUIDs).

Each `RefundLineItem` carries:

- `returnId` — stable key for row-level actions (e.g. a disclosed status badge, deeplink to the stop).
- `sequence` + `customerName` — the stop the return was captured at (an auto-added walk-in stop for a customer not originally on the trek still appears here).
- `invoiceId` / `invoiceNumber` — the originating invoice (nullable only in the degenerate case where the stop has no invoice recorded yet; the driver return endpoint only accepts returns tied to an invoice, so this is effectively always populated).
- `productId`, `productName`, `basicQtyReturned`, `packagingQtyReturned` — the returned stock. `packagingQtyReturned` is `null` for products with no packaging unit.
- `refundAmount`, `refundMethod` — nullable until the batch is captured; present on all driver-token returns.
- `approvalStatus` — `Pending` / `Approved` / `Rejected`.
- `reason` — the driver-supplied note from capture (max 500 chars).
- `rejectionReason` — populated only when `approvalStatus = Rejected`.
- `recordedAt` — when the driver captured the return.
- `approvedAt` — populated for both `Approved` and `Rejected` decisions.

Totals in `summary.totalPendingRefunds` / `totalApprovedRefunds` / `totalRejectedRefunds` are the sum of `refundAmount` grouped by `approvalStatus` on this same list; counts match. Pending refunds have **no** effect on `totalCollected`, `netCashOnHand`, `basicQtyRemaining` or the customer ledger until an admin approves them.

## Refresh and PDF download

Fetch when opening the report and after delivery recording, stock-load edits, completion, or return decisions. Show `generatedAt` so users know the report's freshness. Cache keys must include the token, and cached offline reports should be marked stale. Do not assume final figures while returns are pending.

The PDF endpoint builds the same live report and names the attachment `TrekReport-{trekNumber}-{scheduledDate}.pdf`. It is separate from the trekking delivery sheet and frontend-generated invoice PDFs. The PDF layout mirrors the JSON: a three-block **Financial Summary** (Trek P&L, Money Position, Deferred & Exposure), **Collections by Payment Method**, **Stop Breakdown** (with per-stop payment-method chips), a flat **Refunds** table (one row per return with status colour-coded Approved=green, Pending=amber, Rejected=red and the recorded timestamp + rejection reason inline), and the basic-unit **Stock Reconciliation** table. Example using browser Fetch:

```js
async function downloadDriverReport(token) {
  const response = await fetch(`/api/v1/treks/driver/${token}/report/pdf`);
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message ?? 'Unable to download report');
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = 'trek-report.pdf';
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
```

The sample supplies a local fallback filename; use the server's attachment filename when exposed by the deployment's response headers. See [invoices](./22-sale-invoices.md), [returns](./23-invoice-returns.md), and [warehouse stock](./24-vehicle-warehouse.md).
