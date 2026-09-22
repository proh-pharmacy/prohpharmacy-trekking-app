# 09 — Customers

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/customers` | List customers (paginated, filterable) |
| `GET` | `api/v1/customers/map-pins` | All customer locations for a map (unpaginated) |
| `GET` | `api/v1/customers/{id}` | Get a single customer |
| `POST` | `api/v1/customers` | Register a new customer |
| `POST` | `api/v1/customers/import` | Bulk import customers from Excel |
| `PATCH` | `api/v1/customers/{id}` | Update customer business details |
| `POST` | `api/v1/customers/{customerId}/locations` | Add an additional location |
| `PATCH` | `api/v1/customers/{customerId}/locations/{locationId}` | Update a location |
| `DELETE` | `api/v1/customers/{customerId}/locations/{locationId}` | Delete a location |
| `POST` | `api/v1/customers/{customerId}/people/{personId}/portrait` | Upload representative portrait |
| `POST` | `api/v1/customers/{customerId}/premises-photo` | Upload business premises photo |
| `POST` | `api/v1/customers/sync` | Batch sync offline-created customers |
| `GET` | `api/v1/customers/markups` | Get all customer markup rules (across all customers) |
| `GET` | `api/v1/customers/{customerId}/markups` | Get markup rules for a specific customer |
| `PUT` | `api/v1/customers/{customerId}/markups` | Create or update a markup rule |
| `DELETE` | `api/v1/customers/{customerId}/markups/{markupId}` | Remove a markup rule |

---

## Enum Reference

### `customerType`
`RetailPharmacy` `WholesalePharmacy` `OTCMedicineSeller` `Clinic` `Hospital` `ChemicalShop` `LicensedHealthFacility` `Other`

### `relationshipType`
`Owner` `Proprietor` `Director` `Manager` `PrimaryContact` `CreditResponsiblePerson` `Guarantor` `Other`

### `registrationStatus`
`Draft` `PendingReview` `Active` `Rejected` `Suspended` `Inactive`

### `locationType`
`BusinessPremises` `DeliveryLocation` `Residential` `Other`

> Always send enum values as strings, not integers.

---

## GET /api/v1/customers/map-pins

Returns one pin per GPS-recorded location. A customer with multiple GPS locations appears as multiple pins — one per location. Use `locationId` + `customerAccountId` together to uniquely identify each pin. `isPrimary` tells you which pin is the primary location. Region and district filters match any location, not just the primary.

`regionId` and `regionName` on each pin reflect the **location's own region**, not the customer's account registration region. A customer registered in Ahafo with a delivery location in Greater Accra will emit a pin with `regionName: "Greater Accra Region"` for that delivery location.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `branchId` | `guid` | Scope to customers belonging to a specific branch |
| `regionId` | `guid` | Scope to customers with any location in a specific region |
| `districtId` | `guid` | Scope to customers with any location in a specific district |

### Response `200 OK`

```json
[
  {
    "locationId": "...",
    "customerAccountId": "...",
    "customerCode": "GAR-00001",
    "businessName": "Accra Pharmacy Ltd",
    "tradingName": "Accra Pharma",
    "customerType": "RetailPharmacy",
    "registrationStatus": "Active",
    "primaryPhoneNumber": "+233201234567",
    "isPrimary": true,
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5,
    "streetAddress": "12 Liberation Road, Accra",
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "branchId": "...",
    "branchName": "Tema Branch",
    "regionId": "...",
    "regionName": "Greater Accra Region",
    "primaryContactName": "Ama Boateng",
    "primaryContactPhone": "+233209876543",
    "primaryContactPortraitUrl": "https://ik.imagekit.io/..."
  },
  {
    "locationId": "...",
    "customerAccountId": "...",
    "customerCode": "GAR-00001",
    "businessName": "Accra Pharmacy Ltd",
    "isPrimary": false,
    "latitude": 5.5483,
    "longitude": -0.2074,
    "streetAddress": "Makola Street, Accra",
    "..."
  }
]
```

### Leaflet example

```js
const res = await fetch('/api/v1/customers/map-pins', {
    headers: { Authorization: `Bearer ${token}` }
})
const pins = await res.json()

pins.forEach(p => {
    L.marker([p.latitude, p.longitude])
        .addTo(map)
        .bindPopup(`
            <strong>${p.businessName}</strong><br>
            <small>${p.customerCode} · ${p.customerType}</small><br>
            ${p.primaryContactName ?? ''}<br>
            ${p.streetAddress ?? p.landmarkAndDirections ?? ''}
        `)
})
```

### Marker colour by customer type

```js
const typeColours = {
    RetailPharmacy:         '#00bf6f',
    WholesalePharmacy:      '#0284c7',
    OTCMedicineSeller:      '#f59e0b',
    Clinic:                 '#8b5cf6',
    Hospital:               '#dc2626',
    ChemicalShop:           '#64748b',
    LicensedHealthFacility: '#0891b2',
    Other:                  '#94a3b8'
}

function getCustomerIcon(customerType) {
    const colour = typeColours[customerType] ?? '#94a3b8'
    return L.divIcon({
        className: '',
        html: `<div style="
            width:12px; height:12px; border-radius:50%;
            background:${colour}; border:2px solid #fff;
            box-shadow:0 1px 3px rgba(0,0,0,.4)">
        </div>`,
        iconSize: [12, 12],
        iconAnchor: [6, 6]
    })
}
```

---

## GET /api/v1/customers

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by business name, customer code, or phone number |
| `sort` | `string` | e.g. `createdAt_desc`, `businessName_asc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `regionId` | `guid` | Filter by region |
| `districtId` | `guid` | Filter by district (matched against primary location) |
| `branchId` | `guid` | Filter by owning branch |
| `customerType` | `string` | e.g. `RetailPharmacy` |
| `status` | `string` | e.g. `Active`, `PendingReview` |

### Response `200 OK` — `PaginatedData<CustomerResponse>`

---

## GET /api/v1/customers/{id}

### Response `200 OK`

```json
{
  "id": "...",
  "customerCode": "GAR-00001",
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "registrationStatus": "Active",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233201234567",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "owningBranchId": "...",
  "owningBranchName": "Tema Branch",
  "registeredByStaffId": "...",
  "registeredByName": "Kwame Asante",
  "registeredDuringTrekId": null,
  "createdOffline": false,
  "premisesPhotoUrl": "https://ik.imagekit.io/prohpharmacy/customers/premises/abc.jpg",
  "recordedAt": "2026-09-09T10:00:00Z",
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null,
  "primaryPerson": {
    "id": "...",
    "fullName": "Ama Boateng",
    "relationshipType": "Owner",
    "primaryPhoneNumber": "+233209876543",
    "isPrimaryContact": true,
    "isCreditResponsiblePerson": true
  },
  "primaryLocation": {
    "id": "...",
    "locationType": "BusinessPremises",
    "regionId": "...",
    "regionName": "Greater Accra Region",
    "districtId": "...",
    "districtName": "Accra",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5,
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "streetAddress": "12 Liberation Road, Accra",
    "captureMethod": "PwaGps",
    "verificationStatus": "GpsCaptured",
    "isPrimary": true
  },
  "additionalLocations": [
    {
      "id": "...",
      "locationType": "DeliveryLocation",
      "regionId": "...",
      "regionName": "Greater Accra Region",
      "districtId": "...",
      "districtName": "Accra",
      "latitude": 5.5483,
      "longitude": -0.2074,
      "accuracyMetres": 8.0,
      "landmarkAndDirections": "Near Makola Market, ask for Ama",
      "streetAddress": "Makola Street, Accra",
      "captureMethod": "PwaGps",
      "verificationStatus": "GpsCaptured",
      "isPrimary": false
    }
  ]
}
```

`additionalLocations` contains every non-primary location recorded for this customer (delivery points, residential, etc.). The array is empty `[]` when only the primary location exists. Each item has the same shape as `primaryLocation` — including `id`, `locationType`, `regionId`, `districtId`, and `districtName` so the frontend can pre-populate the edit form. Both the list endpoint and the single GET endpoint return this array.

### Errors
- `404` — customer not found

---

## POST /api/v1/customers

Customer, representative, and primary location are created in a single request.

### Request body

```json
{
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "regionId": "<region-guid>",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233201234567",
  "registeredDuringTrekId": null,
  "openingBalance": 250.00,
  "representative": {
    "firstName": "Ama",
    "middleName": null,
    "lastName": "Boateng",
    "relationshipType": "Owner",
    "primaryPhoneNumber": "+233209876543",
    "ghanaCardNumber": "GHA-123456789-0"
  },
  "location": {
    "districtId": "<district-guid>",
    "streetAddress": "12 Liberation Road, Accra",
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5
  }
}
```

### Field rules

**Business fields:**

| Field | Required | Constraints |
|---|---|---|
| `businessName` | Yes | Max 200 chars |
| `customerType` | Yes | String — see enum table |
| `regionId` | Yes | Must exist |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |
| `registeredDuringTrekId` | No | Trek ID if registered during a visit |
| `openingBalance` | No | Decimal. Amount the customer owes. Must be greater than 0 if provided. Creates an initial Debit ledger entry. |

**Representative fields:**

| Field | Required | Constraints |
|---|---|---|
| `firstName` | Yes | Max 80 chars |
| `lastName` | Yes | Max 80 chars |
| `relationshipType` | Yes | String — see enum table |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `middleName` | No | Max 80 chars |
| `ghanaCardNumber` | No | Max 30 chars |

**Location fields:**

| Field | Required | Constraints |
|---|---|---|
| `districtId` | Yes | Must exist |
| `landmarkAndDirections` | No | Max 500 chars |
| `streetAddress` | No | Max 300 chars |
| `latitude` | Yes | -90 to 90 |
| `longitude` | Yes | -180 to 180 |
| `accuracyMetres` | Yes | Must be >= 0. `0` means manual map selection |

### Notes
- `owningBranch` is **auto-resolved** from the authenticated staff member's branch — do not send it
- `customerCode` is **auto-generated** in format `{REGION_CODE}-{SEQUENCE:D5}` e.g. `GAR-00001`
- `captureMethod` is set to `PwaGps` when `accuracyMetres > 0`, otherwise `ManualLocationSelection`
- `verificationStatus` is set to `GpsCaptured` for GPS, `Unverified` for manual
- `registrationStatus` defaults to `Active`

### Response `201 Created` — full `CustomerResponse` (same as GET single)

### Errors
- `422` — validation error
- `404` — region or district not found

---

## PATCH /api/v1/customers/{id}

Updates business details, primary representative, and primary location in a single request. Omit `representative` or `location` to leave them unchanged.

### Request body

```json
{
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "regionId": "<region-guid>",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233209999999",
  "representative": {
    "firstName": "Ama",
    "middleName": null,
    "lastName": "Boateng",
    "relationshipType": "Owner",
    "primaryPhoneNumber": "+233209876543",
    "ghanaCardNumber": "GHA-123456789-0"
  },
  "location": {
    "districtId": "<district-guid>",
    "streetAddress": "12 Liberation Road, Accra",
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5
  }
}
```

**Business fields:**

| Field | Required | Constraints |
|---|---|---|
| `businessName` | Yes | Max 200 chars |
| `regionId` | Yes | If changed, `customerCode` is regenerated for the new region |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `customerType` | Yes | String — see enum table |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |

**`representative` — omit to leave unchanged:**

| Field | Required | Constraints |
|---|---|---|
| `firstName` | Yes | Max 80 chars |
| `lastName` | Yes | Max 80 chars |
| `relationshipType` | Yes | String — see enum table |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `middleName` | No | Max 80 chars |
| `ghanaCardNumber` | No | Max 30 chars |

**`location` — omit to leave unchanged:**

| Field | Required | Constraints |
|---|---|---|
| `districtId` | Yes | Must exist |
| `streetAddress` | No | Max 300 chars |
| `landmarkAndDirections` | No | Max 500 chars |
| `latitude` | No | -90 to 90 |
| `longitude` | No | -180 to 180 |
| `accuracyMetres` | No | Must be > 0 if provided |

### Response `200 OK` — full `CustomerResponse`

### Errors
- `404` — customer, region, or district not found
- `422` — validation error

---

## POST /api/v1/customers/{customerId}/locations

### Request body

```json
{
  "locationType": "DeliveryLocation",
  "regionId": "<region-guid>",
  "districtId": "<district-guid>",
  "landmarkAndDirections": "Near Makola Market, ask for Ama",
  "streetAddress": "Makola Street, Accra",
  "latitude": 5.5483,
  "longitude": -0.2074,
  "accuracyMetres": 8.0,
  "isPrimary": false
}
```

| Field | Required | Constraints |
|---|---|---|
| `regionId` | Yes | |
| `districtId` | Yes | |
| `landmarkAndDirections` | No | Max 500 chars |
| `locationType` | No | String. Defaults to `BusinessPremises` |
| `streetAddress` | No | Max 300 chars |
| `latitude` | No | -90 to 90 |
| `longitude` | No | -180 to 180 |
| `accuracyMetres` | No | Must be > 0 if provided |
| `isPrimary` | No | Set `true` to make this the new primary location |

### Response `201 Created`

```json
{
  "id": "...",
  "customerAccountId": "...",
  "locationType": "DeliveryLocation",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "districtId": "...",
  "districtName": "Accra",
  "landmarkAndDirections": "Near Makola Market, ask for Ama",
  "streetAddress": "Makola Street, Accra",
  "latitude": 5.5483,
  "longitude": -0.2074,
  "accuracyMetres": 8.0,
  "captureMethod": "PwaGps",
  "verificationStatus": "GpsCaptured",
  "isPrimary": false,
  "createdAt": "2026-09-09T10:00:00Z"
}
```

### Errors
- `404` — customer or district not found
- `422` — validation error

---

## PATCH /api/v1/customers/{customerId}/locations/{locationId}

Only fields present in the request body are applied — omit a field to leave it unchanged.

### Request body

```json
{
  "locationType": "DeliveryLocation",
  "districtId": "<district-guid>",
  "streetAddress": "Makola Street, Accra",
  "landmarkAndDirections": "Near Makola Market",
  "latitude": 5.5483,
  "longitude": -0.2074,
  "accuracyMetres": 8.0,
  "isPrimary": true
}
```

| Field | Required | Notes |
|---|---|---|
| `locationType` | No | `BusinessPremises`, `DeliveryLocation`, `Residential`, `Other` |
| `districtId` | No | UUID |
| `streetAddress` | No | Max 300 chars |
| `landmarkAndDirections` | No | Max 500 chars |
| `latitude` | No | -90 to 90. When provided, `longitude` and `accuracyMetres` should be sent too |
| `longitude` | No | -180 to 180 |
| `accuracyMetres` | No | > 0 |
| `isPrimary` | No | `true` — demotes the current primary and promotes this location. `false` — demotes this location |

When GPS coordinates are sent, `captureMethod` is set to `PwaGps` and `verificationStatus` to `GpsCaptured`.

### Response `200 OK` — `LocationResponse` (same shape as POST)

### Errors
- `404` — location not found on this customer
- `422` — validation error

---

## DELETE /api/v1/customers/{customerId}/locations/{locationId}

Permanently removes the location. Any location can be deleted, including the primary.

### Response `204 No Content`

### Errors
- `404` — location not found on this customer

---

## POST /api/v1/customers/{customerId}/people/{personId}/portrait

`multipart/form-data` upload. `personId` comes from `primaryPerson.id` in the customer response.

| Constraint | Value |
|---|---|
| Accepted types | JPEG, PNG, WebP |
| Max size | 5 MB |
| Field name | `file` |

### Response `200 OK`

```json
{
  "personId": "...",
  "portraitUrl": "https://ik.imagekit.io/..."
}
```

### Errors
- `404` — person not found on this customer
- `422` — unsupported file type or file exceeds 5 MB

---

## POST /api/v1/customers/{customerId}/premises-photo

`multipart/form-data` upload of a photo of the customer's business premises. Optional — upload separately after registration. Replaces any existing premises photo.

| Constraint | Value |
|---|---|
| Accepted types | JPEG, PNG, WebP |
| Max size | 5 MB |
| Field name | `file` |

### Response `200 OK`

```json
{
  "customerId": "...",
  "premisesPhotoUrl": "https://ik.imagekit.io/prohpharmacy/customers/premises/abc.jpg"
}
```

The `premisesPhotoUrl` is also reflected on the customer's full response from this point on.

### Errors
- `404` — customer not found
- `422` — unsupported file type or file exceeds 5 MB

---

## POST /api/v1/customers/import

Bulk-imports customers from an Excel file. The user specifies which column header maps to each field — the file layout is flexible and not fixed.

### Request

`Content-Type: multipart/form-data`

**Required form fields:**

| Field | Description |
|---|---|
| `file` | `.xlsx` or `.xls` file |
| `businessNameColumn` | Exact header text for business name, e.g. `"Business Name"` |
| `customerTypeColumn` | Exact header text for customer type, e.g. `"Type"` |
| `regionNameColumn` | Exact header text for region name, e.g. `"Region"` |
| `primaryPhoneColumn` | Exact header text for primary phone, e.g. `"Phone"` |
| `repFirstNameColumn` | Exact header text for representative first name |
| `repLastNameColumn` | Exact header text for representative last name |
| `repPhoneColumn` | Exact header text for representative phone |
| `repRelationshipColumn` | Exact header text for representative relationship type |

**Optional form fields:**

| Field | Description |
|---|---|
| `tradingNameColumn` | Trading/DBA name |
| `whatsAppColumn` | WhatsApp number |
| `repMiddleNameColumn` | Representative middle name |
| `ghanaCardColumn` | Ghana Card number |
| `districtNameColumn` | District name — matched within the region |
| `streetAddressColumn` | Street address |
| `landmarkColumn` | Landmark and directions |
| `openingBalanceColumn` | Opening balance amount — creates an initial ledger entry per customer |

Header matching is **case-insensitive**. Omitting an optional column field leaves that field `null` on the created record.

### How the file is processed

1. Row 1 is the header row. All specified column names are located by text match.
2. Every row from row 2 onwards is processed. Blank business name rows are silently skipped.
3. **Region** is matched by name (case-insensitive). Rows with an unrecognised region are skipped.
4. **District** (if column provided) is matched by name within the matched region. Rows where the district name is not found in that region are skipped.
5. **CustomerType** must match one of the valid enum values (case-insensitive). Rows with an invalid type are skipped.
6. **RepRelationship** is matched case-insensitively. Defaults to `Owner` if blank or unrecognised.
7. `CustomerCode` is auto-generated per region in format `{REGION_CODE}-{SEQUENCE:D5}` e.g. `GAR-00001`.
8. `owningBranch` is resolved from the authenticated staff member's branch.
9. **Opening balance** (if column provided): amount the customer owes. Must be a positive number — zero, negative, and non-numeric values are ignored. Creates a Debit ledger entry.
10. All valid rows are committed in a single transaction.

> Provide region and district names exactly as configured in the Organisation settings. The frontend should show a reference list of valid names before the user starts filling their spreadsheet.

### Valid enum values

**`customerType`:** `RetailPharmacy` `WholesalePharmacy` `OTCMedicineSeller` `Clinic` `Hospital` `ChemicalShop` `LicensedHealthFacility` `Other`

**`repRelationship`:** `Owner` `Proprietor` `Director` `Manager` `PrimaryContact` `CreditResponsiblePerson` `Guarantor` `Other` (defaults to `Owner` if blank)

### Response `200 OK`

```json
{
  "imported": 45,
  "skipped": 3,
  "openingBalancesCreated": 42,
  "skippedRows": [
    "Row 4 (Tema Pharmacy): region 'Accra' not found.",
    "Row 9 (Koforidua Clinic): invalid CustomerType 'Pharmacy'.",
    "Row 14 (Cape Coast Drug Store): representative first name, last name, and phone are required."
  ]
}
```

| Field | Description |
|---|---|
| `imported` | Number of customers successfully created |
| `skipped` | Number of rows skipped |
| `openingBalancesCreated` | Number of opening balance ledger entries created (≤ `imported`) |
| `skippedRows` | Descriptions of each skipped row including the row number and business name |

### Errors
- `422` — no file provided, unsupported file type, missing required form fields, or specified column header not found in the file

---

## Customer Markup Rules

Customer-specific pricing markup rules override region markup rules when a sale is made to that customer. See [20-pricing-markups.md](./20-pricing-markups.md) for the full pricing resolution logic.

A rule with no `productId` applies to **all products** for that customer. A rule with a `productId` applies to **that product only**.

### GET /api/v1/customers/markups

Returns paginated markup rules across all customers. Useful for a global "Pricing Rules" admin view.

#### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Filter by customer name or product name |
| `customerId` | `guid` | Scope to a specific customer |
| `productId` | `guid` | See all customers that have a rule for a specific product |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: unpaginated if omitted |

#### Response `200 OK` — `PaginatedData<CustomerMarkupRuleResponse>`

Results are ordered by customer name, then customer-wide rules first, then by product name.

---

### GET /api/v1/customers/{customerId}/markups

Returns paginated markup rules for the customer. The customer-wide rule (no `productId`) is always listed first, then product-specific rules sorted by product name. Search filters by product name.

#### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Filter by product name |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: unpaginated if omitted |

#### Response `200 OK` — `PaginatedData<CustomerMarkupRuleResponse>`

```json
[
  {
    "id": "...",
    "customerId": "...",
    "customerName": "Accra Pharmacy Ltd",
    "productId": null,
    "productName": null,
    "markupPercentage": 5.00,
    "createdAt": "2026-09-22T10:00:00Z",
    "updatedAt": null
  },
  {
    "id": "...",
    "customerId": "...",
    "customerName": "Accra Pharmacy Ltd",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "markupPercentage": -2.50,
    "createdAt": "2026-09-22T10:00:00Z",
    "updatedAt": null
  }
]
```

### PUT /api/v1/customers/{customerId}/markups

Creates or updates a markup rule. Sending the same `productId` (or `null`) again updates the existing rule rather than creating a duplicate.

#### Request body

```json
{
  "productId": "<product-guid-or-null>",
  "markupPercentage": 5.00
}
```

| Field | Required | Constraints |
|---|---|---|
| `productId` | No | Omit (or send `null`) for a customer-wide rule; send a product GUID for a product-specific rule |
| `markupPercentage` | Yes | Between `-99.99` and `500`. Positive = price increase, negative = reduction |

#### Response `200 OK` — same shape as list item above

#### Errors
- `404` — customer or product not found
- `422` — validation error

### DELETE /api/v1/customers/{customerId}/markups/{markupId}

Removes the markup rule. The customer reverts to region markup (or base price) for the affected products.

#### Response `204 No Content`

#### Errors
- `404` — rule not found on this customer
