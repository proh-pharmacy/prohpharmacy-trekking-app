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
| `GET` | `api/v1/treks/driver/{token}/region/treks` | All active treks in the region |
| `GET` | `api/v1/treks/driver/{token}/offline/products` | Product catalogue seed (supports `?since=`) |
| `GET` | `api/v1/treks/driver/{token}/offline/customers` | Customers in the region seed (supports `?since=`) |
| `GET` | `api/v1/treks/driver/{token}/offline/trek` | Full assigned trek with stops + returns (supports `?since=`) |
| `POST` | `api/v1/treks/driver/{token}/customers` | Register a new customer from the field |
| `POST` | `api/v1/treks/driver/{token}/treks/{trekId}/stops` | Add a walk-in stop to any active trek in the region |
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/products/unplanned` | Add an unplanned product sale at a stop |
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/returns` | Record a product return at a stop |
| `DELETE` | `api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId}` | Void a return |
| `POST` | `api/v1/treks/driver/{token}/sync` | Push all queued offline actions in one batch |

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

**Response `201 Created`** — full `CustomerResponse` (same shape as `POST /api/v1/customers`).

**Errors:**
- `422` — validation failed, or phone number already registered

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
| `representative.firstName` | Yes | |
| `representative.lastName` | Yes | |
| `representative.relationshipType` | Yes | |
| `representative.primaryPhoneNumber` | Yes | |
| `gps` | No | Optional — see GPS section |

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

## Notes

- **No JWT required** — the driver token is the only credential. None of the driver portal endpoints check the `Authorization` header.
- **Trek locked after Completion** — once a trek is `Completed`, returns and unplanned sales can no longer be added. Batch sync returns `Conflict` for actions targeting a completed trek.
- **Batch size** — no enforced limit, but keep batches under 200 actions for predictable response times.
- **Delta sync cadence** — call the `?since=` endpoints after every successful batch push to keep the local trek copy current. Store `lastSyncedAt` in localStorage.
- **Conflicts** — a `Conflict` result does not fail the batch. Other actions in the same request are still processed. Surface conflicts to the driver with a clear message (e.g. "This customer's phone number is already registered — tap to link to the existing record").
