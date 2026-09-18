# 11 — Trekking

## Endpoints

| Method | Endpoint | Purpose | Auth |
|---|---|---|---|
| `POST` | `api/v1/treks` | Create a trek | Required |
| `GET` | `api/v1/treks` | List treks (paginated, filterable) | Required |
| `GET` | `api/v1/treks/{id}` | Get a single trek | Required |
| `PATCH` | `api/v1/treks/{id}/status` | Change trek status | Required |
| `POST` | `api/v1/treks/{trekId}/stops` | Add a stop to a trek | Required |
| `PATCH` | `api/v1/treks/{trekId}/stops/{stopId}` | Update a stop's sequence, notes, or products | Required |
| `DELETE` | `api/v1/treks/{trekId}/stops/{stopId}` | Remove a stop | Required |
| `POST` | `api/v1/treks/{id}/record` | Record deliveries (admin) | Required |
| `PATCH` | `api/v1/treks/{trekId}/stops/{stopId}/products/{stopProductId}/price` | Override snapshotted price on a stop product | Required |
| `POST` | `api/v1/treks/{trekId}/sync-prices` | Re-sync all stop product prices from the current catalog | Required |
| `GET` | `api/v1/treks/{trekId}/price-diff` | Get price differences between trek and current catalog | Required |
| `POST` | `api/v1/treks/{id}/generate-link` | Generate shareable driver link | Required |
| `POST` | `api/v1/treks/{id}/send-email` | Email trek sheet to staff | Required |
| `GET` | `api/v1/treks/{id}/sheet/pdf` | Download trek sheet PDF | Required |
| `GET` | `api/v1/treks/driver/{token}` | Get trek via driver token | None |
| `GET` | `api/v1/treks/driver/{token}/sheet/pdf` | Download delivery sheet PDF via driver token | None |
| `POST` | `api/v1/treks/driver/{token}/record` | Record deliveries via driver token | None |
| `GET` | `api/v1/treks/sheet/preview` | Preview sample trek sheet PDF | None |

---

## Enum Reference

### `trekStatus`
`Draft` `Scheduled` `InProgress` `Completed` `Cancelled`

### `paymentMethod`
`Cash` `MobileMoney` `Credit` `Cheque` `BankTransfer`

> Always send enum values as strings, not integers.

---

## Shared Response Shape — `TrekResponse`

Used by create, get single, get list, and status change.

```json
{
  "id": "...",
  "trekNumber": "TRK-00001",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "branchId": null,
  "branchName": null,
  "driverStaffId": "...",
  "driverName": "Kwame Asante",
  "salesStaffId": null,
  "salesStaffName": null,
  "vehicleId": "...",
  "vehicleDisplayName": "Sprinter Van 1",
  "scheduledDate": "2026-09-15",
  "status": "Draft",
  "notes": "Morning route",
  "syncRequired": false,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null,
  "stops": [
    {
      "stopId": "...",
      "sequence": 1,
      "customerAccountId": "...",
      "customerName": "Accra Pharmacy Ltd",
      "customerCode": "GAR-00001",
      "customerPhone": "0244123456",
      "customerType": "RetailPharmacy",
      "regionName": "Greater Accra",
      "districtName": "Tema",
      "primaryLocationLandmark": "Next to Accra Mall, ground floor",
      "primaryLocationStreet": "12 Liberation Road, Accra",
      "latitude": 5.6032,
      "longitude": -0.1869,
      "accuracyMetres": 12.5,
      "primaryContactName": "Ama Boateng",
      "primaryContactPhone": "0209876543",
      "notes": null,
      "products": [
        {
          "stopProductId": "...",
          "productId": "...",
          "productName": "Paracetamol 500mg",
          "basicUnitName": "Tab",
          "packagingUnitName": "Box",
          "basicUnitPrice": 2.50,
          "packagingUnitPrice": 60.00,
          "plannedBasicQuantity": 10,
          "plannedPackagingQuantity": 2,
          "basicQtyDelivered": null,
          "packagingQtyDelivered": null,
          "amountDue": 145.00,
          "paymentMethod": null,
          "amtPaid": null,
          "balance": null,
          "notes": null,
          "deliveredAt": null
        }
      ]
    }
  ]
}
```

`basicUnitPrice` and `packagingUnitPrice` are snapshotted at the time the stop is added — they will not change if the product price is later updated in the system.

`amountDue` is the planned total for the line item: `plannedBasicQuantity × basicUnitPrice + plannedPackagingQuantity × packagingUnitPrice`. It is recalculated if an admin uses the price override or sync-prices endpoint.

`syncRequired` is `true` on `GET /api/v1/treks/{id}` when at least one stop product's snapshotted price or packaging configuration is out of date with the current catalog. Use this to dynamically show a "Sync Prices" button on the trek detail page. `syncRequired` is always `false` on the list endpoint and on create — it is only computed on the single trek fetch.

> The list endpoint (`GET /api/v1/treks`) returns treks with `stops: []` — stops are only populated on the single get (`GET /api/v1/treks/{id}`).

---

## POST /api/v1/treks

### Request body

```json
{
  "regionId": "<region-guid>",
  "branchId": "<branch-guid>",
  "scheduledDate": "2026-09-15",
  "vehicleId": "<vehicle-guid>",
  "salesStaffId": "<staff-guid>",
  "notes": "Morning route"
}
```

| Field | Required | Constraints |
|---|---|---|
| `regionId` | Yes | The region this trek covers |
| `branchId` | No | Optional branch association |
| `scheduledDate` | Yes | `DateOnly` format (`YYYY-MM-DD`) |
| `vehicleId` | Yes | Must have an active driver assigned |
| `salesStaffId` | No | Optional sales/records staff on the trek |
| `notes` | No | Max 500 chars |

### Notes
- `trekNumber` is auto-generated in format `TRK-{SEQUENCE:D5}` e.g. `TRK-00001`
- `status` defaults to `Draft`
- The driver is **auto-inferred** from the vehicle's active staff assignment — do not send a `driverStaffId`. Returns `422` if the vehicle has no active driver.

### Response `201 Created` — `TrekResponse`

### Errors
- `422` — validation error or referenced entity not found

---

## GET /api/v1/treks

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by trek number or driver name |
| `sort` | `string` | e.g. `scheduledDate_desc`, `createdAt_asc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `regionId` | `guid` | Filter by region |
| `branchId` | `guid` | Filter by branch |
| `status` | `string` | e.g. `Draft`, `InProgress` |
| `scheduledDate` | `DateOnly` | Filter by exact scheduled date (`YYYY-MM-DD`) |

### Response `200 OK` — `PaginatedData<TrekResponse>`

---

## GET /api/v1/treks/{id}

Returns the trek with all stops and their products fully populated.

### Response `200 OK` — `TrekResponse`

### Errors
- `404` — trek not found

---

## PATCH /api/v1/treks/{id}/status

### Request body

```json
{
  "status": "Scheduled"
}
```

Valid status values: `Draft` `Scheduled` `InProgress` `Completed` `Cancelled`

### Response `200 OK` — `TrekResponse`

### Errors
- `404` — trek not found
- `422` — invalid status value

---

## POST /api/v1/treks/{trekId}/stops

### Request body

```json
{
  "customerAccountId": "<customer-guid>",
  "sequence": 1,
  "notes": "Call ahead before arriving",
  "products": [
    {
      "productId": "<product-guid>",
      "plannedBasicQuantity": 10,
      "plannedPackagingQuantity": 2
    },
    {
      "productId": "<product-guid>",
      "plannedBasicQuantity": 5
    }
  ]
}
```

| Field | Required | Constraints |
|---|---|---|
| `customerAccountId` | Yes | Must exist |
| `sequence` | Yes | Must be > 0 |
| `products` | Yes | At least one item |
| `products[].productId` | Yes | Must exist |
| `products[].plannedBasicQuantity` | No | >= 0 when provided |
| `products[].plannedPackagingQuantity` | No | >= 0 when provided; only stored if the product has a packaging unit |
| `notes` | No | Max 500 chars |

At least one of `plannedBasicQuantity` or `plannedPackagingQuantity` must be > 0 per product line.

If the product has no packaging unit, `plannedPackagingQuantity` is silently ignored.

### Response `201 Created`

```json
{
  "stopId": "...",
  "sequence": 1,
  "customerAccountId": "...",
  "customerName": "Accra Pharmacy Ltd",
  "customerCode": "GAR-00001",
  "customerPhone": "0244123456",
  "customerType": "RetailPharmacy",
  "regionName": "Greater Accra",
  "districtName": "Tema",
  "primaryLocationLandmark": "Next to Accra Mall, ground floor",
  "primaryLocationStreet": "12 Liberation Road, Accra",
  "latitude": 5.6032,
  "longitude": -0.1869,
  "accuracyMetres": 12.5,
  "primaryContactName": "Ama Boateng",
  "primaryContactPhone": "0209876543",
  "notes": "Call ahead before arriving",
  "products": [
    {
      "stopProductId": "...",
      "productId": "...",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tab",
      "packagingUnitName": "Box",
      "basicUnitPrice": 2.50,
      "packagingUnitPrice": 60.00,
      "plannedBasicQuantity": 10,
      "plannedPackagingQuantity": 2,
      "basicQtyDelivered": null,
      "packagingQtyDelivered": null,
      "amountDue": 145.00,
      "paymentMethod": null,
      "amtPaid": null,
      "balance": null,
      "notes": null,
      "deliveredAt": null
    }
  ]
}
```

`packagingUnitName`, `packagingUnitPrice`, and `plannedPackagingQuantity` are `null` when the product has no packaging unit. Prices are snapshotted at stop-creation time.

### Errors
- `404` — trek, customer, or product not found
- `422` — validation error

---

## PATCH /api/v1/treks/{trekId}/stops/{stopId}

Updates a stop's sequence position, notes, or product list. If `products` is omitted the existing product lines are left untouched. If `products` is provided it replaces all existing products for that stop and re-snapshots prices from the current product catalogue.

### Request body

```json
{
  "sequence": 2,
  "notes": "Updated delivery notes",
  "products": [
    {
      "productId": "<product-guid>",
      "plannedBasicQuantity": 10,
      "plannedPackagingQuantity": 2
    }
  ]
}
```

| Field | Required | Constraints |
|---|---|---|
| `sequence` | No | > 0 |
| `notes` | No | Max 500 chars |
| `products` | No | If provided, must be non-empty; at least one qty > 0 per product |
| `products[].productId` | Yes (if products provided) | Must exist |
| `products[].plannedBasicQuantity` | No | >= 0 when provided |
| `products[].plannedPackagingQuantity` | No | >= 0 when provided |

### Response `200 OK` — same `TrekStopResponse` shape as `POST /api/v1/treks/{trekId}/stops`

### Errors
- `404` — trek or stop not found
- `422` — validation error or product not found

---

## DELETE /api/v1/treks/{trekId}/stops/{stopId}

### Response `204 No Content`

### Errors
- `404` — trek or stop not found

---

## POST /api/v1/treks/{id}/record

Records delivery outcomes for one or more stop products. Can be submitted multiple times — each call updates only the provided `stopProductId` entries. Trek must not be `Completed`.

### Request body

```json
{
  "products": [
    {
      "stopProductId": "<stop-product-guid>",
      "basicQtyDelivered": 8,
      "packagingQtyDelivered": 1,
      "paymentMethod": "Cash",
      "amtPaid": 240.00,
      "balance": 0.00,
      "notes": "Customer short 2 units"
    }
  ]
}
```

| Field | Required | Constraints |
|---|---|---|
| `stopProductId` | Yes | Must belong to this trek |
| `basicQtyDelivered` | No | Decimal — units delivered (e.g. tablets) |
| `packagingQtyDelivered` | No | Decimal — packages delivered (e.g. boxes). Only meaningful when the product has a packaging unit |
| `paymentMethod` | No | See enum table |
| `amtPaid` | No | Decimal. If omitted and quantities were delivered, auto-calculated from delivered qty × snapshotted price |
| `balance` | No | Decimal. Set to `0` automatically when `amtPaid` is auto-calculated |
| `notes` | No | Free text |

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1
}
```

### How payment recording works

1. The driver enters the delivered basic and packaging quantities.
2. `amtPaid` is **optional** — do not force the user to fill it in.
3. If `amtPaid` is omitted, the backend auto-calculates the amount using the snapshotted prices:
   ```
   basicQtyDelivered × basicUnitPrice + packagingQtyDelivered × packagingUnitPrice
   ```
   and sets `balance` to `0`.
4. If the customer paid only **part** of the amount, the frontend sends an explicit `amtPaid` (what was collected) and `balance` (what is still owed). The backend stores both as provided.
5. `amountDue` (from the product listing) is the **planned** total based on planned quantities — it is display-only and should **not** be submitted during delivery recording.

> **Frontend guidance:** leave the amount field empty by default. Only show/require it when the driver indicates a partial or different payment. This allows the auto-calculation to handle the normal full-payment case without the driver doing manual arithmetic.

### Notes
- Recording does **not** touch the ledger. Ledger entries are only written when the trek is marked `Completed`. Re-submitting updated figures before completion is safe — the ledger will reflect the final values at completion time.

---

## PATCH /api/v1/treks/{trekId}/stops/{stopId}/products/{stopProductId}/price

Corrects the snapshotted `basicUnitPrice` and/or `packagingUnitPrice` on a specific stop product. Use this when a price was entered incorrectly at the time the stop was created. `amountDue` is recalculated automatically. Not allowed once the trek is `Completed`.

### Request body

```json
{
  "basicUnitPrice": 3.00,
  "packagingUnitPrice": 72.00
}
```

| Field | Required | Notes |
|---|---|---|
| `basicUnitPrice` | Yes | New price for the basic unit |
| `packagingUnitPrice` | No | New price for the packaging unit. Send `null` to clear |

### Response `200 OK` — `TrekStopProductResponse` with updated prices and recalculated `amountDue`

### Errors
- `404` — trek, stop, or stop product not found
- `422` — trek is already `Completed`

### Errors
- `404` — trek not found
- `422` — trek is already `Completed`

---

## POST /api/v1/treks/{id}/generate-link

Generates (or retrieves) a persistent driver token for this trek. The token becomes read-only once the trek is `Completed`.

### Response `200 OK`

```json
{
  "token": "a1b2c3d4-...",
  "url": "https://app.example.com/driver/a1b2c3d4-..."
}
```

### Errors
- `404` — trek not found

---

## GET /api/v1/treks/driver/{token}

Anonymous endpoint for the driver's mobile form. Returns real-time delivery state.

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "scheduledDate": "2026-09-15",
  "driverName": "Kwame Asante",
  "vehicleDisplayName": "Sprinter Van 1",
  "regionName": "Greater Accra Region",
  "salesStaffId": null,
  "salesStaffName": null,
  "status": "InProgress",
  "isLocked": false,
  "stops": [
    {
      "stopId": "...",
      "sequence": 1,
      "customerName": "Accra Pharmacy Ltd",
      "customerCode": "GAR-00001",
      "primaryPhoneNumber": "+233201234567",
      "customerType": "Retail",
      "regionName": "Greater Accra",
      "districtName": "Accra Metropolitan",
      "primaryLocationLandmark": "Next to Accra Mall, ground floor",
      "primaryLocationStreet": "12 Liberation Road, Accra",
      "latitude": 5.6032,
      "longitude": -0.1869,
      "accuracyMetres": 12.5,
      "primaryContactName": "Kofi Mensah",
      "primaryContactPhone": "+233241234567",
      "notes": null,
      "products": [
        {
          "stopProductId": "...",
          "productName": "Paracetamol 500mg",
          "basicUnitName": "Tab",
          "packagingUnitName": "Box",
          "basicUnitPrice": 2.50,
          "packagingUnitPrice": 60.00,
          "plannedBasicQuantity": 10,
          "plannedPackagingQuantity": 2,
          "basicQtyDelivered": 8,
          "packagingQtyDelivered": 1,
          "paymentMethod": "Cash",
          "amtPaid": 240.00,
          "balance": 0.00,
          "notes": "Customer short 2 units",
          "deliveredAt": "2026-09-15T09:45:00Z"
        }
      ]
    }
  ]
}
```

- `isLocked` is `true` when trek status is `Completed` — driver cannot submit further deliveries.

### Errors
- `404` — token not found

---

## GET /api/v1/treks/driver/{token}/sheet/pdf

Anonymous endpoint. Downloads the delivery sheet as a PDF using the driver token.

Returns a `application/pdf` file attachment named `TrekkingSheet-{trekNumber}-{scheduledDate}.pdf`. The PDF reflects live data — if the driver has already recorded deliveries, those quantities, payment method, and balance will appear in the sheet.

**Usage:** render a download button on the driver portal that opens or downloads this URL directly. No `Authorization` header needed.

```
GET /api/v1/treks/driver/3fa85f64-5717-4562-b3fc-2c963f66afa6/sheet/pdf
```

### Errors
- `404` — token not found or invalid

---

## POST /api/v1/treks/driver/{token}/record

Anonymous delivery recording via driver link. Identical request/response shape to the admin record endpoint.

### Request body

Same as `POST /api/v1/treks/{id}/record`

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1
}
```

### Errors
- `404` — token not found
- `422` — trek is already `Completed`

---

## POST /api/v1/treks/{id}/send-email

Emails the trek sheet (with PDF attachment and driver form link) to one or more staff members. Auto-generates the driver token if one does not yet exist.

### Request body

```json
{
  "staffIds": [
    "<staff-guid-1>",
    "<staff-guid-2>"
  ]
}
```

| Field | Required | Constraints |
|---|---|---|
| `staffIds` | Yes | At least one staff member ID |

### Response `200 OK`

```json
{
  "trekNumber": "TRK-00001",
  "sent": 2,
  "recipients": [
    "Kwame Asante <kwame@prohpharmacy.com>",
    "Ama Boateng <ama@prohpharmacy.com>"
  ]
}
```

### Errors
- `404` — trek not found
- `422` — no valid recipients found

---

## GET /api/v1/treks/{id}/sheet/pdf

Downloads the trek sheet as a PDF.

- **Response:** Binary PDF file
- **Content-Disposition:** `attachment; filename="TrekkingSheet-TRK-00001-2026-09-15.pdf"`

### Errors
- `404` — trek not found

---

## GET /api/v1/treks/sheet/preview

Returns a sample trek sheet PDF with placeholder data. No authentication required. Useful for previewing the template during development.

- **Response:** Binary PDF file
