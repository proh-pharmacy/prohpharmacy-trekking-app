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
    "netCashOnHand": 50,
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
| `netCashOnHand` | Cash collected + MobileMoney collected − approved **Cash** refunds |
| `stopsVisited` | Stops with any product having basic delivered quantity > 0 or amount paid > 0 |
| `basicQtyRemaining` | `max(0, loaded − delivered + approved returns)` per trek-load product |

Label net cash clearly: it includes MobileMoney and does not subtract MobileMoney refunds. It is not a physical-cash-only balance. Collections come from product records, so independent customer ledger payments are not included.

`collectionsByMethod` contains only methods with positive paid entries, sorted by amount descending; do not expect zero-valued rows for every method. Stops are ordered by sequence. Each stop's `paymentMethods` is a de-duplicated, alphabetically-sorted list of methods actually used to pay on that stop (products with `amtPaid > 0`); render it as a chip row (e.g. `Cash · MobileMoney`) so mixed-payment stops read correctly. Return lists show all approval states, but totals and stock reconciliation use approved returns only.

Each stop's `amountDue`, like the trek-level `totalSalesValue`, only counts products that have been delivered — a stop with planned-but-undelivered products shows `amountDue = 0` until the first delivery is recorded, and `amountDue − amtPaid === balance` always holds.

Stock reconciliation covers **only products with trek stock-load rows**, using basic units only. It omits unallocated products and packaging quantities. It is a trek allocation calculation, not the live vehicle warehouse balance. Stop rows do not include sold-product line items or IDs; use the existing trek detail data if the UI needs those details. The phase notes' richer per-product stop breakdown is not in this DTO.

## Refresh and PDF download

Fetch when opening the report and after delivery recording, stock-load edits, completion, or return decisions. Show `generatedAt` so users know the report's freshness. Cache keys must include the token, and cached offline reports should be marked stale. Do not assume final figures while returns are pending.

The PDF endpoint builds the same live report and names the attachment `TrekReport-{trekNumber}-{scheduledDate}.pdf`. It is separate from the trekking delivery sheet and frontend-generated invoice PDFs. Example using browser Fetch:

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
