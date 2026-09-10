# 15 — Reports

## Overview

Three report modules, each with a raw JSON endpoint (for building tables/charts) and an Excel export endpoint (for download). All endpoints require authentication.

---

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/reports/treks` | Trek performance report (JSON, paginated) |
| `GET` | `api/v1/reports/treks/export` | Trek report download (.xlsx) |
| `GET` | `api/v1/reports/collections` | Collections by payment method and branch (JSON) |
| `GET` | `api/v1/reports/collections/export` | Collections report download (.xlsx) |
| `GET` | `api/v1/reports/products` | Product delivery report (JSON, paginated) |
| `GET` | `api/v1/reports/products/export` | Product delivery report download (.xlsx) |

---

## GET /api/v1/reports/treks

Trek-level summary with per-trek collection and outstanding totals.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Filter by `ScheduledDate` (inclusive), e.g. `2026-01-01` |
| `to` | `date?` | Filter by `ScheduledDate` (inclusive) |
| `branchId` | `guid?` | Filter by branch |
| `driverId` | `guid?` | Filter by driver (staff ID) |
| `status` | `string?` | `Draft`, `Scheduled`, `InProgress`, `Completed`, or `Cancelled` |
| `search` | `string?` | Search by trek number, driver name, or branch name |
| `sort` | `string?` | `scheduledDate_asc`, `scheduledDate_desc` (default), `trekNumber_asc/desc`, `collected_asc/desc`, `outstanding_asc/desc` |
| `pageNumber` | `int?` | Defaults to `1` |
| `pageSize` | `int?` | Defaults to `20`, max `100` |

### Response `200 OK`

```json
{
  "totalTreks": 120,
  "completed": 98,
  "cancelled": 5,
  "inProgress": 3,
  "totalCollected": 85000.00,
  "totalOutstanding": 12000.00,
  "page": 1,
  "pageSize": 20,
  "totalCount": 120,
  "totalPages": 6,
  "treks": [
    {
      "id": "...",
      "trekNumber": "TRK-00042",
      "scheduledDate": "2026-09-10",
      "driverName": "Kwame Asante",
      "branchName": "Tema Branch",
      "status": "Completed",
      "stopsCount": 8,
      "totalCollected": 3200.00,
      "totalOutstanding": 450.00
    }
  ]
}
```

### Field notes

| Field | Notes |
|---|---|
| `totalTreks` / `completed` / etc. | Summary counts across **all** filtered results, not just the current page |
| `totalCollected` / `totalOutstanding` | Aggregate across all filtered results. Per-trek figures are sums of `AmtPaid` and `Balance` across all stop products |
| `status` | One of `Draft`, `Scheduled`, `InProgress`, `Completed`, `Cancelled` |

---

## GET /api/v1/reports/treks/export

Downloads a styled `.xlsx` trek performance report. Accepts the same filters as the JSON endpoint (except `search`, `sort`, `pageNumber`, `pageSize`). Status cells are color-coded: green = Completed, red = Cancelled, amber = InProgress.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Filter by `ScheduledDate` |
| `to` | `date?` | Filter by `ScheduledDate` |
| `branchId` | `guid?` | Filter by branch |
| `driverId` | `guid?` | Filter by driver |
| `status` | `string?` | Filter by status |

Returns `TrekReport-{YYYYMMDD}.xlsx`.

### Usage

```js
const response = await fetch(
  '/api/v1/reports/treks/export?from=2026-01-01&to=2026-09-30',
  { headers: { Authorization: `Bearer ${token}` } }
);
const disposition = response.headers.get('Content-Disposition');
const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? 'TrekReport.xlsx';
const blob = await response.blob();
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url; a.download = filename; a.click();
URL.revokeObjectURL(url);
```

---

## GET /api/v1/reports/collections

Returns total credit collections grouped by payment method and by branch. No pagination — aggregated data only.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Start date (inclusive), filters by `recordedAt` |
| `to` | `date?` | End date (inclusive), filters by `recordedAt` |
| `branchId` | `guid?` | Filter by customer's owning branch |
| `regionId` | `guid?` | Filter by customer's region |

### Response `200 OK`

```json
{
  "from": "2026-01-01",
  "to": "2026-09-30",
  "totalCollected": 95000.00,
  "totalTransactions": 312,
  "byPaymentMethod": [
    { "paymentMethod": "Cash", "total": 45000.00, "transactions": 150 },
    { "paymentMethod": "MobileMoney", "total": 30000.00, "transactions": 100 },
    { "paymentMethod": "Cheque", "total": 12000.00, "transactions": 42 },
    { "paymentMethod": "BankTransfer", "total": 8000.00, "transactions": 20 }
  ],
  "byBranch": [
    { "branchName": "Tema Branch", "total": 50000.00, "transactions": 170 },
    { "branchName": "Accra Central", "total": 45000.00, "transactions": 142 }
  ]
}
```

### Field notes

| Field | Notes |
|---|---|
| `paymentMethod` | Values from Credit ledger entries. `"Unspecified"` means a credit entry with no payment method recorded |
| `totalCollected` | Sum of all Credit ledger entry amounts matching the filters |
| `from` / `to` | Echo of the requested date range; `null` if not supplied |

---

## GET /api/v1/reports/collections/export

Downloads a styled `.xlsx` collections report with two sections — By Payment Method and By Branch — each with a totals row.

### Query parameters

Same as `GET /api/v1/reports/collections`.

Returns `CollectionsReport-{YYYYMMDD}.xlsx`.

### Usage

Same fetch pattern as other export endpoints — send the Bearer token and read the filename from `Content-Disposition`.

---

## GET /api/v1/reports/products

Product delivery performance across completed treks, grouped by product.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Filter by trek `ScheduledDate` (inclusive) |
| `to` | `date?` | Filter by trek `ScheduledDate` (inclusive) |
| `branchId` | `guid?` | Filter by trek's branch |
| `productId` | `guid?` | Narrow to a single product — returns one item in `products[]` |
| `search` | `string?` | Search by product name (ignored when `productId` is set) |
| `pageNumber` | `int?` | Defaults to `1` |
| `pageSize` | `int?` | Defaults to `20`, max `100` |

Only products from **Completed** treks with `qtyDelivered > 0` are included.

### Response `200 OK`

```json
{
  "totalProductLines": 85,
  "totalAmountCollected": 72000.00,
  "totalOutstanding": 8500.00,
  "page": 1,
  "pageSize": 20,
  "totalCount": 85,
  "totalPages": 5,
  "products": [
    {
      "productId": "...",
      "productName": "Paracetamol 500mg",
      "unit": "Strips",
      "totalQtyDelivered": 1200.0,
      "totalCollected": 15000.00,
      "totalOutstanding": 800.00,
      "treksCount": 18
    }
  ]
}
```

### Field notes

| Field | Notes |
|---|---|
| `totalProductLines` | Count of distinct products across all matching deliveries (after search filter) |
| `totalAmountCollected` / `totalOutstanding` | Aggregates across **all** filtered products, not just the current page |
| `treksCount` | Number of distinct completed treks in which this product was delivered |
| `totalQtyDelivered` | Sum of `qtyDelivered` across all delivery records for this product |

---

## GET /api/v1/reports/products/export

Downloads a styled `.xlsx` product delivery report sorted by total collected (highest first). Outstanding amounts are highlighted red when > 0.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `from` | `date?` | Filter by trek `ScheduledDate` |
| `to` | `date?` | Filter by trek `ScheduledDate` |
| `branchId` | `guid?` | Filter by branch |
| `productId` | `guid?` | Narrow to a single product |

Returns `ProductDeliveryReport-{YYYYMMDD}.xlsx`.

### Usage

Same fetch pattern as other export endpoints — send the Bearer token and read the filename from `Content-Disposition`.
