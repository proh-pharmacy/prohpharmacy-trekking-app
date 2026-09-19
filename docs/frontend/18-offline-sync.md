# 18 — Trek Driver Control Panel & Offline Sync

## Overview

The driver token (the UUID sent in trek assignment emails) is the sole credential for a full **field control panel**. No login, no JWT. The driver opens their unique link and gets an operational interface for their assigned trek and all active treks in their region.

Because routes pass through areas with no connectivity, the control panel works **fully offline**. The driver queues actions locally and pushes them in one batch when connectivity returns.

---

## The Token

The `DriverToken` on `TrekkingTrip` is a UUID generated when the admin sends the trek assignment email. It never changes.

**From the token the backend always derives:**

| Value | Source |
|---|---|
| Trek | `TrekkingTrips WHERE DriverToken = token` |
| Region | `trek.RegionId` |
| Attribution | `trek.SalesStaffId ?? trek.DriverStaffId` (sales rep first, driver second) |

All actions recorded through the token are attributed to the sales rep if one is assigned, otherwise the driver. This is reflected on ledger entries, return records, and customer registrations.

---

## Endpoint Map

All driver portal endpoints use `api/v1/treks/driver/{token}/...` and require **no authorization header** — `.AllowAnonymous()` is set on all of them.

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/treks/driver/{token}` | Driver's assigned trek (existing) |
| `GET` | `api/v1/treks/driver/{token}/assigned` | All active treks assigned to this driver |
| `GET` | `api/v1/treks/driver/{token}/region/treks` | All active treks in the region |
| `POST` | `api/v1/treks/driver/{token}/treks/{trekId}/generate-token` | Get or generate a token for another trek |
| `GET` | `api/v1/treks/driver/{token}/offline/products` | Product catalogue seed (supports `?since=`) |
| `GET` | `api/v1/treks/driver/{token}/offline/customers` | Customers in the region seed (supports `?since=`) |
| `GET` | `api/v1/treks/driver/{token}/offline/trek` | Full assigned trek with stops + returns (supports `?since=`) |
| `POST` | `api/v1/treks/driver/{token}/customers` | Register a new customer from the field |
| `PATCH` | `api/v1/customers/{id}` *(online only)* | Update an existing customer (requires auth — use `UpdateCustomer` batch action for offline) |
| `POST` | `api/v1/treks/driver/{token}/treks/{trekId}/stops` | Add a walk-in stop to any active trek in the region |
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/products/unplanned` | Add an unplanned product sale at a stop |
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/returns` | Record a product return at a stop |
| `DELETE` | `api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId}` | Void a return |
| `POST` | `api/v1/treks/driver/{token}/complete` | Mark the trek as completed (triggers ledger sync) |
| `POST` | `api/v1/treks/driver/{token}/sync` | Push all queued offline actions in one batch |
| `GET` | `api/v1/treks/driver/{token}/device` | Last known device position, battery, speed, motion |
| `POST` | `api/v1/treks/driver/{token}/location` | Report current GPS location to Traccar |
| `POST` | `api/v1/treks/driver/{token}/sos` | Send SOS alert via Traccar |
| `POST` | `api/v1/treks/driver/{token}/customers/{customerId}/premises-photo` | Upload premises photo for a customer |
| `POST` | `api/v1/treks/driver/{token}/customers/{customerId}/people/{personId}/portrait` | Upload representative portrait |

---

## Control Panel Views

### GET /api/v1/treks/driver/{token}

Returns the driver's assigned trek with all stops, products, and returns. See [doc 11](./11-trekking.md) for the full response shape. New fields on this response:

- `stops[].isWalkIn` — `true` for stops added mid-trek
- `stops[].returns[]` — list of product returns recorded at this stop
- `stops[].products[].isUnplanned` — `true` for products added outside the original plan

### GET /api/v1/treks/driver/{token}/region/treks

All active (`Scheduled` or `InProgress`) treks in the same region.

**Response `200 OK`**

```json
[
  {
    "trekId": "...",
    "trekNumber": "TRK-00042",
    "scheduledDate": "2026-09-17",
    "status": "InProgress",
    "driverName": "Kwame Asante",
    "salesStaffName": null,
    "regionName": "Greater Accra Region",
    "stopsCount": 8
  }
]
```

### GET /api/v1/treks/driver/{token}/assigned

Returns all `Scheduled` and `InProgress` treks where this driver is the assigned driver — across all regions. Use this to power the **"My Treks"** tab alongside the existing `GET .../region/treks` **"Region Treks"** tab.

**Response `200 OK`** — same shape as `GET .../region/treks`:

```json
[
  {
    "trekId": "...",
    "trekNumber": "TRK-00042",
    "scheduledDate": "2026-09-19",
    "status": "InProgress",
    "driverName": "Kwame Asante",
    "salesStaffName": null,
    "regionName": "Greater Accra Region",
    "stopsCount": 8
  }
]
```

---

### POST /api/v1/treks/driver/{token}/treks/{trekId}/generate-token

Generates (or retrieves the existing) driver token for any trek from either tab. Use this when the driver wants to switch their current working trek.

- If the trek already has a token, the existing token is returned unchanged
- If no token exists yet, a new one is generated and saved
- The trek must be assigned to this driver **or** be active in the same region — returns `422` otherwise

**Response `200 OK`**

```json
{
  "token": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "url": "https://trekking.prohpharmacy.com/treks/driver?token=3fa85f64-..."
}
```

**Frontend switch flow:**

```js
// Driver taps a trek from either tab
async function switchTrek(trekId) {
  const res = await fetch(
    `/api/v1/treks/driver/${currentToken}/treks/${trekId}/generate-token`,
    { method: 'POST' }
  );
  const { token, url } = await res.json();
  // Navigate to the new token — this becomes the driver's working context
  window.location.href = url;
}
```

**Errors:**
- `404` — current token or target trek not found
- `422` — target trek is not assigned to this driver and not in the same region

---

## Online Individual Actions

These endpoints process one action immediately. Use them when the device is online.

### POST /api/v1/treks/driver/{token}/customers

Register a new customer from the field. Pass `clientGeneratedId` for offline idempotency — if that ID already exists the existing customer is returned without creating a duplicate.

**Request body**

```json
{
  "businessName": "Koforidua Pharmacy",
  "customerType": "RetailPharmacy",
  "primaryPhoneNumber": "0244123456",
  "tradingName": null,
  "whatsAppNumber": null,
  "clientGeneratedId": "<device-uuid>",
  "representative": {
    "firstName": "Ama",
    "lastName": "Boateng",
    "middleName": null,
    "relationshipType": "Owner",
    "primaryPhoneNumber": "0244123456"
  },
  "gps": {
    "latitude": 6.0835,
    "longitude": -0.2170,
    "accuracyMetres": 12.5
  }
}
```

| Field | Required | Notes |
|---|---|---|
| `businessName` | Yes | Max 200 chars |
| `customerType` | Yes | See enum reference in doc 09 |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `clientGeneratedId` | No | UUID — deduplication key for offline idempotency |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |
| `representative.firstName` | Yes | Max 80 chars |
| `representative.lastName` | Yes | Max 80 chars |
| `representative.relationshipType` | Yes | See enum reference in doc 09 |
| `representative.primaryPhoneNumber` | Yes | Max 30 chars |
| `representative.middleName` | No | |
| `gps` | No | Entire object optional — omit if device has no fix |
| `gps.latitude` | Yes (if gps) | -90 to 90 |
| `gps.longitude` | Yes (if gps) | -180 to 180 |
| `gps.accuracyMetres` | Yes (if gps) | ≥ 0 |

**Notes:**
- `regionId` is derived from the token — the customer is automatically assigned to the trek's region.
- `owningBranchId` and `registeredByStaffId` are derived from the trek's attribution staff member.
- `districtId` is not required — GPS-only location records are valid.
- `createdOffline: true` is always set on customers registered via this endpoint.

**Response `201 Created`** — full `CustomerResponse` (same shape as `POST /api/v1/customers`). Includes `premisesPhotoUrl` once uploaded.

**Errors:**
- `422` — validation failed, or phone number already registered

---

### POST /api/v1/treks/driver/{token}/customers/{customerId}/premises-photo

Upload a photo of the customer's business premises. Call this **after** customer creation once you have the server `customerId` (from the registration response or a completed batch sync). This is a multipart upload — it cannot be queued in the offline batch.

**Request:** `multipart/form-data` with a single field `file` (JPEG, PNG, or WebP, max 5 MB).

**Response `200 OK`**

```json
{
  "customerId": "...",
  "premisesPhotoUrl": "https://ik.imagekit.io/prohpharmacy/customers/premises/abc.jpg"
}
```

The URL is also reflected on the customer's `premisesPhotoUrl` field from that point on.

**Note:** GPS capture and premises photo are independent — a customer can have GPS coordinates without a photo, or a photo without coordinates. Both are optional.

---

### POST /api/v1/treks/driver/{token}/customers/{customerId}/people/{personId}/portrait

Upload a portrait photo for the customer's representative. `personId` comes from `primaryPerson.id` in the customer registration response. The customer must belong to the trek's region.

**Request:** `multipart/form-data` with a single field `file` (JPEG, PNG, or WebP, max 5 MB).

**Response `200 OK`**

```json
{
  "personId": "...",
  "portraitUrl": "https://ik.imagekit.io/prohpharmacy/customers/portraits/abc.jpg"
}
```

Like premises photo, this is a **separate follow-up request** — upload after you have the server `personId` from the registration response.

---

### POST /api/v1/treks/driver/{token}/treks/{trekId}/stops

Add a walk-in stop to any active trek in the region. `isWalkIn` is always `true` for stops created via this endpoint.

**Request body**

```json
{
  "customerAccountId": "<existing-server-customer-guid>",
  "sequence": 5,
  "notes": "Met on the main road",
  "clientGeneratedId": "<device-uuid>",
  "gps": {
    "latitude": 6.0835,
    "longitude": -0.2170,
    "accuracyMetres": 12.5
  }
}
```

| Field | Required | Notes |
|---|---|---|
| `customerAccountId` | Yes | Must be an existing server-side customer ID |
| `sequence` | Yes | > 0 |
| `notes` | No | Max 500 chars |
| `clientGeneratedId` | No | Deduplication key |
| `gps` | No | Optional — stored on the stop for location reference |

**Response `201 Created`** — `TrekStopResponse` (same shape as stops in trek response).

**Errors:**
- `404` — trek not found in this region
- `422` — trek is Completed or Cancelled

---

### POST /api/v1/treks/driver/{token}/stops/{stopId}/products/unplanned

Add a product sale at a stop that was not in the original plan. The product is recorded as delivered in real time — `isUnplanned: true` on the resulting stop product.

**Request body**

```json
{
  "productId": "...",
  "basicQtyDelivered": 10,
  "packagingQtyDelivered": null,
  "paymentMethod": "Cash",
  "amtPaid": 250.00,
  "balance": 0.00,
  "notes": null,
  "clientGeneratedId": "<device-uuid>"
}
```

| Field | Required | Notes |
|---|---|---|
| `productId` | Yes | |
| `basicQtyDelivered` | Yes | > 0 |
| `packagingQtyDelivered` | No | Only relevant if the product has a packaging unit |
| `paymentMethod` | No | `Cash`, `MobileMoney`, `Cheque`, `BankTransfer` |
| `amtPaid` | No | ≥ 0 |
| `balance` | No | ≥ 0 |
| `notes` | No | Max 500 chars |
| `clientGeneratedId` | No | Deduplication key |

**Response `201 Created`** — `TrekStopProductResponse`.

---

### POST /api/v1/treks/driver/{token}/stops/{stopId}/returns

Record a product return at a stop. The returned product does not have to be from the current trek — a customer may return something from a previous delivery. Unit prices are snapshotted from the product catalogue at the time of recording.

**Request body**

```json
{
  "productId": "...",
  "basicQtyReturned": 2,
  "packagingQtyReturned": null,
  "refundAmount": 50.00,
  "refundMethod": "Cash",
  "reason": "Damaged packaging",
  "clientGeneratedId": "<device-uuid>",
  "gps": {
    "latitude": 6.0835,
    "longitude": -0.2170,
    "accuracyMetres": 18.0
  }
}
```

| Field | Required | Notes |
|---|---|---|
| `productId` | Yes | |
| `basicQtyReturned` | Yes | > 0 |
| `packagingQtyReturned` | No | Only if product has a packaging unit |
| `refundAmount` | No | Actual amount refunded — ≥ 0 |
| `refundMethod` | No | `Cash`, `MobileMoney`, `Cheque`, `BankTransfer` |
| `reason` | No | Max 500 chars |
| `clientGeneratedId` | No | Deduplication key — idempotent |
| `gps` | No | Optional GPS at time of return |

**Response `201 Created`**

```json
{
  "returnId": "...",
  "productId": "...",
  "productName": "Paracetamol 500mg",
  "basicUnitName": "Strips",
  "packagingUnitName": null,
  "basicQtyReturned": 2,
  "packagingQtyReturned": null,
  "basicUnitPrice": 25.00,
  "packagingUnitPrice": null,
  "refundAmount": 50.00,
  "refundMethod": "Cash",
  "reason": "Damaged packaging",
  "recordedAt": "2026-09-17T10:45:00Z"
}
```

**Ledger impact:** When the trek is marked `Completed`, each return with a `refundAmount > 0` generates a **Debit** entry on the customer's ledger (mirrors how deliveries generate Credit entries for payments received).

---

### DELETE /api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId}

Void a return. Not allowed on Completed or Cancelled treks.

**Response `204 No Content`**

---

## Completing the Trek

### POST /api/v1/treks/driver/{token}/complete

Marks the trek as `Completed` and runs the full ledger sync in a single transaction:
- **Credit** entries for each payment group across all stops (`amtPaid > 0`)
- **Debit** entries for outstanding balances (`balance > 0`)
- **Debit** entries for product return refunds (`refundAmount > 0`)

No request body. The response is the full `TrekResponse`.

**Guards:**
- Trek must be `InProgress` — returns `422` if already `Completed` or `Cancelled`, or if it hasn't been started yet
- Only the driver's own assigned trek can be completed via this endpoint

**Recommended flow:** push a final batch sync first to flush any queued offline actions, then call this endpoint.

```js
async function finishTrek() {
  await syncOfflineQueue();   // flush any pending actions first
  const res = await fetch(`/api/v1/treks/driver/${token}/complete`, { method: 'POST' });
  if (!res.ok) {
    const err = await res.json();
    alert(err.message);
    return;
  }
  // Trek is now locked — redirect driver to summary screen
}
```

**Errors:** `422` if the trek is not `InProgress`.

---

## Offline Seed Data

Before going into the field, seed the local store with the following. All three endpoints support `?since=ISO8601` for **delta sync** — pass the last-fetched timestamp to download only records modified since then.

```js
// Full seed on first load
await seedLocal('products',   `/api/v1/treks/driver/${token}/offline/products`);
await seedLocal('customers',  `/api/v1/treks/driver/${token}/offline/customers`);
await seedLocal('trek',       `/api/v1/treks/driver/${token}/offline/trek`);

// Delta on reconnect
const since = localStorage.getItem('lastSyncedAt');
await seedLocal('products',  `/api/v1/treks/driver/${token}/offline/products?since=${since}`);
```

### GET .../offline/products

Full product catalogue — name, unit prices, basic and packaging unit names.

### GET .../offline/customers

All customers in the trek's region — names, phone numbers, GPS coordinates, primary contact. Used for the customer search/select when adding walk-in stops.

### GET .../offline/trek

Full trek with all stops, products, and returns. Use this to populate the offline working copy. After a batch sync, re-fetch this to apply the server's resolved IDs.

---

## GPS Capture

GPS works from the device hardware chip — **no mobile data, no WiFi, no SIM required**.

```js
function getGps() {
  return new Promise((resolve) => {
    if (!navigator.geolocation) { resolve(null); return; }
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({
        latitude:       pos.coords.latitude,
        longitude:      pos.coords.longitude,
        accuracyMetres: pos.coords.accuracy
      }),
      () => resolve(null),   // permission denied or no fix — action still proceeds
      { enableHighAccuracy: true, timeout: 8000 }
    );
  });
}
```

**GPS is captured on:** `RegisterCustomer` (stored as customer location), `AddWalkInStop` (stored on stop), `RecordReturn` (stored on return record).
**GPS is NOT captured on:** `RecordDelivery` or `RecordUnplannedSale` — the stop already has a location.
**Never block an action on GPS failure.** Pass `gps: null` and the action goes through without coordinates.

---

## Offline Batch Sync

### POST /api/v1/treks/driver/{token}/sync

When connectivity returns, push all queued actions in one request. Actions are processed in `occurredAt` order. Re-submitting the same batch is safe — every `clientId` is stored and duplicates return `AlreadySynced`.

### Request

```json
{
  "actions": [
    {
      "type": "RegisterCustomer",
      "clientId": "<device-uuid-1>",
      "occurredAt": "2026-09-17T10:32:00Z",
      "payload": {
        "businessName": "Koforidua Pharmacy",
        "primaryPhoneNumber": "0244123456",
        "customerType": "RetailPharmacy",
        "representative": {
          "firstName": "Ama",
          "lastName": "Boateng",
          "relationshipType": "Owner",
          "primaryPhoneNumber": "0244123456"
        },
        "gps": { "latitude": 6.0835, "longitude": -0.2170, "accuracyMetres": 12.5 }
      }
    },
    {
      "type": "AddWalkInStop",
      "clientId": "<device-uuid-2>",
      "occurredAt": "2026-09-17T10:35:00Z",
      "payload": {
        "trekId": "<trek-guid>",
        "customerClientId": "<device-uuid-1>",
        "sequence": 5,
        "notes": "Met on the main road"
      }
    },
    {
      "type": "RecordDelivery",
      "clientId": "<device-uuid-3>",
      "occurredAt": "2026-09-17T10:38:00Z",
      "payload": {
        "stopProductId": "<existing-server-stop-product-guid>",
        "basicQtyDelivered": 10,
        "paymentMethod": "Cash",
        "amtPaid": 250.00,
        "balance": 0.00
      }
    },
    {
      "type": "RecordUnplannedSale",
      "clientId": "<device-uuid-4>",
      "occurredAt": "2026-09-17T10:40:00Z",
      "payload": {
        "stopClientId": "<device-uuid-2>",
        "productId": "<product-guid>",
        "basicQtyDelivered": 10,
        "paymentMethod": "Cash",
        "amtPaid": 250.00,
        "balance": 0.00
      }
    },
    {
      "type": "RecordReturn",
      "clientId": "<device-uuid-5>",
      "occurredAt": "2026-09-17T10:45:00Z",
      "payload": {
        "stopId": "<existing-server-stop-guid>",
        "productId": "<product-guid>",
        "basicQtyReturned": 2,
        "refundAmount": 50.00,
        "refundMethod": "Cash",
        "reason": "Damaged packaging",
        "gps": { "latitude": 6.0835, "longitude": -0.2170, "accuracyMetres": 18.0 }
      }
    },
    {
      "type": "VoidReturn",
      "clientId": "<device-uuid-6>",
      "occurredAt": "2026-09-17T10:46:00Z",
      "payload": {
        "returnClientId": "<device-uuid-5>"
      }
    }
  ]
}
```

### Action types and payload fields

**`RegisterCustomer`**

| Field | Required | Notes |
|---|---|---|
| `businessName` | Yes | |
| `primaryPhoneNumber` | Yes | |
| `customerType` | Yes | |
| `tradingName` | No | |
| `whatsAppNumber` | No | |
| `representative.firstName` | Yes | |
| `representative.lastName` | Yes | |
| `representative.middleName` | No | |
| `representative.relationshipType` | Yes | |
| `representative.primaryPhoneNumber` | Yes | |
| `representative.ghanaCardNumber` | No | |
| `gps` | No | Optional — see GPS section |

**`UpdateCustomer`**

Updates an existing customer's account fields, primary representative, and/or GPS location. Only fields present in the payload are applied — everything else is left unchanged. Naturally idempotent.

| Field | Required | Notes |
|---|---|---|
| `customerId` | Yes | Server ID of the customer to update |
| `businessName` | No | New business name |
| `tradingName` | No | Send `null` to clear |
| `primaryPhoneNumber` | No | Returns `Conflict` if the number is already used by another customer |
| `whatsAppNumber` | No | Send `null` to clear |
| `customerType` | No | See enum reference in doc 09 |
| `representative.firstName` | No | |
| `representative.middleName` | No | Send `null` to clear |
| `representative.lastName` | No | |
| `representative.primaryPhoneNumber` | No | |
| `representative.relationshipType` | No | |
| `representative.ghanaCardNumber` | No | Send `null` to clear |
| `gps` | No | Updates the existing primary location if one exists; creates one if not |

**GPS update behaviour:** if the customer already has a primary location its coordinates are overwritten in place. If no location exists yet a new primary `BusinessPremises` location is created. The `gps` object requires `latitude`, `longitude`, and optionally `accuracyMetres`.

> `UpdateCustomer` does not have an `AlreadySynced` path — it is idempotent by nature (applying the same values twice produces the same result). The response always returns `Created` with the customer's `serverId`.

**`AddWalkInStop`**

| Field | Required | Notes |
|---|---|---|
| `trekId` | No | Defaults to the token's own trek |
| `customerClientId` | Either/or | Use when the customer was registered offline in this same batch or a previous batch |
| `customerId` | Either/or | Use when the customer already exists on the server |
| `sequence` | Yes | > 0 |
| `notes` | No | |

**`RecordDelivery`**

Records the delivery outcome for a **planned** stop product (one that already exists in the trek plan). This is the offline equivalent of the existing driver delivery page. `stopProductId` is the server ID of the `TrekkingTripStopProduct` row — it is available in the offline trek seed (`stops[].products[].stopProductId`). Only fields present in the payload are applied; omitted fields keep their current values.

| Field | Required | Notes |
|---|---|---|
| `stopProductId` | Yes | Server ID of the planned stop product to update |
| `basicQtyDelivered` | No | Actual qty delivered |
| `packagingQtyDelivered` | No | Only if product has a packaging unit |
| `paymentMethod` | No | `Cash`, `MobileMoney`, `Cheque`, `BankTransfer` |
| `amtPaid` | No | ≥ 0 |
| `balance` | No | ≥ 0 |
| `notes` | No | Max 500 chars |

**Note:** `RecordDelivery` is an update, not a create. It is naturally idempotent — applying the same values twice produces the same result. `serverId` in the response echoes back the `stopProductId`.

**`RecordUnplannedSale`**

| Field | Required | Notes |
|---|---|---|
| `stopClientId` | Either/or | Use when the stop was added offline in this batch |
| `stopId` | Either/or | Use when the stop already exists on the server |
| `productId` | Yes | |
| `basicQtyDelivered` | Yes | |
| `packagingQtyDelivered` | No | |
| `paymentMethod` | No | |
| `amtPaid` | No | |
| `balance` | No | |

**`RecordReturn`**

| Field | Required | Notes |
|---|---|---|
| `stopId` | Either/or | Server stop ID |
| `stopClientId` | Either/or | Offline stop — resolved from this batch |
| `productId` | Yes | |
| `basicQtyReturned` | Yes | |
| `packagingQtyReturned` | No | |
| `refundAmount` | No | |
| `refundMethod` | No | |
| `reason` | No | Max 500 chars |
| `gps` | No | Optional |

**`VoidReturn`**

Cancels a return that was recorded online or offline. If `returnClientId` matches a `RecordReturn` in the **same batch**, the return is cancelled before it reaches the database — both actions cancel each other out cleanly.

| Field | Required | Notes |
|---|---|---|
| `returnClientId` | Either/or | The `clientId` from the original `RecordReturn` action (offline or previous batch) |
| `returnId` | Either/or | Server return ID — use when the return was recorded online |

### Response `200 OK`

```json
{
  "results": [
    { "clientId": "...", "type": "RegisterCustomer",   "status": "Created",      "serverId": "..." },
    { "clientId": "...", "type": "AddWalkInStop",      "status": "Created",      "serverId": "..." },
    { "clientId": "...", "type": "RecordDelivery",     "status": "Created",      "serverId": "..." },
    { "clientId": "...", "type": "RecordUnplannedSale","status": "Created",      "serverId": null  },
    { "clientId": "...", "type": "RecordReturn",       "status": "Created",      "serverId": "..." },
    { "clientId": "...", "type": "VoidReturn",         "status": "Created",      "serverId": null  }
  ]
}
```

### Per-action statuses

| Status | Meaning |
|---|---|
| `Created` | Action was processed and saved. Use `serverId` to update your local record. |
| `AlreadySynced` | Already processed — existing `serverId` returned. Update your local record with the returned ID. |
| `Conflict` | Could not apply — e.g. phone already registered, trek is Completed, stop not found. Check `reason`. |

### Dependency resolution

The server processes all actions in `occurredAt` order and builds in-memory maps of `clientId → serverId` as it goes:

| Map | Populated by | Consumed by |
|---|---|---|
| `customerClientMap` | `RegisterCustomer` | `AddWalkInStop` via `customerClientId` |
| `stopClientMap` | `AddWalkInStop` | `RecordUnplannedSale` and `RecordReturn` via `stopClientId` |
| `returnClientMap` | `RecordReturn` | `VoidReturn` via `returnClientId` |

Each map also falls back to a DB lookup — so references to records created in **previous batches** resolve correctly too.

### Offline work on other regional treks

The `AddWalkInStop` action accepts any `trekId` from the driver's region — the driver does not have to be assigned to that trek. The `trekId` is available from the regional treks summary seeded by `GET .../offline/region/treks`.

However, the offline trek seed (`GET .../offline/trek`) only covers the **driver's assigned trek**. The stops and products of other regional treks are not seeded. This means:
- Adding a walk-in stop to another trek offline — **supported** (only needs `trekId` + customer)
- Viewing the existing stops of another trek offline — **not supported** (requires connectivity)

---

## Suggested Frontend Flow

```js
// ── Offline action queue ─────────────────────────────────────────────

// When the driver takes an action offline, push to the queue:
async function queueAction(type, payload) {
  const action = {
    type,
    clientId: crypto.randomUUID(),
    occurredAt: new Date().toISOString(),
    payload,
    status: 'pending'
  };
  await localDb.queue.add(action);
  return action.clientId;  // return to caller so they can reference it
}

// Example — register a new customer offline:
const customerClientId = await queueAction('RegisterCustomer', {
  businessName: 'Koforidua Pharmacy',
  primaryPhoneNumber: '0244123456',
  customerType: 'RetailPharmacy',
  representative: { firstName: 'Ama', lastName: 'Boateng',
    relationshipType: 'Owner', primaryPhoneNumber: '0244123456' },
  gps: await getGps()
});

// Example — immediately add a walk-in stop for that offline customer:
const stopClientId = await queueAction('AddWalkInStop', {
  trekId: currentTrekId,
  customerClientId,   // references the customer registered above
  sequence: nextSequence()
});

// Example — record a planned delivery while offline:
await queueAction('RecordDelivery', {
  stopProductId: plannedProduct.stopProductId,  // from offline trek seed
  basicQtyDelivered: 10,
  paymentMethod: 'Cash',
  amtPaid: 250.00,
  balance: 0.00
});

// Example — record an unplanned sale at that offline stop:
const returnClientId = await queueAction('RecordReturn', {
  stopId: existingStopId,
  productId: returnedProductId,
  basicQtyReturned: 2,
  refundAmount: 50.00,
  refundMethod: 'Cash',
  reason: 'Damaged packaging',
  gps: await getGps()
});

// Example — driver realises the return was a mistake, void it immediately:
await queueAction('VoidReturn', { returnClientId });

// Example — record an unplanned sale at the offline walk-in stop:
await queueAction('RecordUnplannedSale', {
  stopClientId,       // references the stop added above
  productId: selectedProductId,
  basicQtyDelivered: 10,
  paymentMethod: 'Cash',
  amtPaid: 250.00,
  balance: 0.00
});

// ── Sync on reconnect ────────────────────────────────────────────────

async function syncOfflineQueue() {
  const pending = await localDb.queue.where({ status: 'pending' }).toArray();
  if (!pending.length) return;

  const response = await fetch(`/api/v1/treks/driver/${token}/sync`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ actions: pending })
  });

  const { results } = await response.json();

  for (const result of results) {
    if (result.status === 'Created' || result.status === 'AlreadySynced') {
      await localDb.queue.update(result.clientId, { status: 'synced', serverId: result.serverId });
    } else {
      await localDb.queue.update(result.clientId, { status: 'conflict', reason: result.reason });
    }
  }

  // Re-seed the local trek copy with server-resolved IDs
  const trek = await fetch(`/api/v1/treks/driver/${token}/offline/trek`).then(r => r.json());
  await localDb.trek.put(trek);

  localStorage.setItem('lastSyncedAt', new Date().toISOString());
}

window.addEventListener('online', syncOfflineQueue);
```

---

## Device Tracking & SOS

### GET /api/v1/treks/driver/{token}/device

Returns the tracking device linked to the trek's vehicle (or the driver's personal device as fallback). Call this on load to populate the status bar. Battery level and live fields come from a live Traccar query; `lastLatitude`/`lastLongitude`/`lastAddress` come from the webhook cache.

**Response `200 OK`**

```json
{
  "deviceId": "...",
  "deviceName": "Van 01 — Accra North",
  "traccarUniqueId": "abc-def-123",
  "lastLatitude": 6.0835,
  "lastLongitude": -0.2170,
  "lastAddress": "Ring Road East, Accra",
  "lastReportedAt": "2026-09-17T10:45:00Z",
  "batteryLevel": 0.72,
  "speed": 0.0,
  "motion": false,
  "ignition": false,
  "traccarStatus": "online"
}
```

`batteryLevel` is `0.0–1.0`. `traccarStatus` reflects the Traccar device status string (`"online"`, `"offline"`, `"unknown"`). All fields except `deviceId`, `deviceName`, and `traccarUniqueId` may be `null` if the device has not reported yet.

**Errors:** `404` if no tracking device is registered for this trek's vehicle or driver.

---

### POST /api/v1/treks/driver/{token}/location

Forwards the driver's GPS coordinates to Traccar via the OsmAnd protocol. Call this on a timer (e.g. every 30–60 seconds) while the app is in the foreground and online. On reconnect after offline, push the most recent known fix.

**Request body**

```json
{
  "latitude": 6.0835,
  "longitude": -0.2170,
  "altitude": 50.0,
  "speed": 8.5,
  "bearing": 180.0,
  "accuracy": 12.5,
  "batteryLevel": 0.75
}
```

| Field | Required | Notes |
|---|---|---|
| `latitude` | Yes | -90 to 90 |
| `longitude` | Yes | -180 to 180 |
| `altitude` | No | Metres — from `coords.altitude` |
| `speed` | No | **m/s** — from `coords.speed` (Geolocation API units, server converts to knots) |
| `bearing` | No | Degrees — from `coords.heading` |
| `accuracy` | No | Metres — from `coords.accuracy` |
| `batteryLevel` | No | **0.0–1.0** — from `navigator.getBattery().then(b => b.level)` |

**Response `204 No Content`**

```js
// Suggested polling pattern
async function reportLocation() {
  const [pos, battery] = await Promise.all([
    new Promise(r => navigator.geolocation.getCurrentPosition(r, () => r(null), { enableHighAccuracy: true })),
    navigator.getBattery?.().catch(() => null)
  ]);
  if (!pos) return;
  await fetch(`/api/v1/treks/driver/${token}/location`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      latitude:     pos.coords.latitude,
      longitude:    pos.coords.longitude,
      altitude:     pos.coords.altitude,
      speed:        pos.coords.speed,
      bearing:      pos.coords.heading,
      accuracy:     pos.coords.accuracy,
      batteryLevel: battery?.level ?? null
    })
  });
}

setInterval(reportLocation, 45_000);
```

---

### POST /api/v1/treks/driver/{token}/sos

Sends a position event to Traccar with `alarm=sos`. Traccar fires an alarm event, which triggers any configured notifications (push, email, SMS, webhooks) to the fleet manager. The driver's current GPS coordinates are required so the alert includes their location.

**Request body**

```json
{
  "latitude": 6.0835,
  "longitude": -0.2170,
  "altitude": 50.0,
  "accuracy": 12.5
}
```

| Field | Required | Notes |
|---|---|---|
| `latitude` | Yes | -90 to 90 |
| `longitude` | Yes | -180 to 180 |
| `altitude` | No | Metres |
| `accuracy` | No | Metres |

**Response `204 No Content`**

**Note:** The SOS is handled entirely by Traccar's notification system. Configure Traccar notifications (via the Traccar web UI or API) to send alerts when `alarm=sos` events arrive for devices in the fleet.

---

### Weather

Weather is a **frontend-only** concern — call a weather API (e.g. OpenWeatherMap) directly from the browser using the current GPS coordinates. No backend endpoint is needed or provided.

```js
// Example: OpenWeatherMap one-call
const weather = await fetch(
  `https://api.openweathermap.org/data/2.5/weather?lat=${lat}&lon=${lon}&appid=${OWM_KEY}&units=metric`
).then(r => r.json());
```

The GPS fix from `navigator.geolocation` provides the coordinates — no extra API call to this backend needed.

---

## Notes

- **No JWT required** — the driver token is the only credential. None of the driver portal endpoints check the `Authorization` header.
- **Trek locked after Completion** — once a trek is `Completed`, returns and unplanned sales can no longer be added. Batch sync returns `Conflict` for actions targeting a completed trek.
- **Batch size** — no enforced limit, but keep batches under 200 actions for predictable response times.
- **Delta sync cadence** — call the `?since=` endpoints after every successful batch push to keep the local trek copy current. Store `lastSyncedAt` in localStorage.
- **Conflicts** — a `Conflict` result does not fail the batch. Other actions in the same request are still processed. Surface conflicts to the driver with a clear message (e.g. "This customer's phone number is already registered — tap to link to the existing record").
