# 08 — Products & Units

## Overview

Units are managed separately as a lookup list. When creating or updating a product, fetch the units list and let the user pick from it — the selected unit ID is stored as a foreign key on the product.

Every product has a **basic unit** (required) and an optional **packaging unit**. Each unit has its own price.

---

## Units Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/units` | List all units |
| `POST` | `api/v1/units` | Create a unit |
| `PUT` | `api/v1/units/{id}` | Rename a unit |
| `PATCH` | `api/v1/units/{id}/status` | Toggle active / inactive |

## Products Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/products` | List products; optionally include, require, or exclude a vehicle's stock catalogue |
| `POST` | `api/v1/products` | Create a product |
| `POST` | `api/v1/products/import` | Bulk import products from Excel |
| `PUT` | `api/v1/products/{id}` | Update a product |
| `PATCH` | `api/v1/products/{id}/packaging-unit` | Set or clear the packaging unit |
| `PATCH` | `api/v1/products/{id}/status` | Toggle active / inactive |

---

## GET /api/v1/units

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by name |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `isActive` | `bool` | `true` = active only, `false` = inactive only |

### Response `200 OK` — `PaginatedData<UnitResponse>`

```json
{
  "id": "...",
  "name": "Strips",
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

---

## POST /api/v1/units

### Request body

```json
{ "name": "Strips" }
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 80 chars, must be unique |

### Response `201 Created` — `UnitResponse`

### Errors
- `422` — name already exists or validation error

---

## PUT /api/v1/units/{id}

### Request body

```json
{ "name": "Cartons" }
```

### Response `200 OK` — `UnitResponse`

### Errors
- `404` — unit not found
- `422` — name already taken or validation error

---

## PATCH /api/v1/units/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `UnitResponse` with updated `isActive`

### Errors
- `404` — unit not found

---

## Shared Response Shape — `ProductResponse`

```json
{
  "id": "...",
  "name": "Paracetamol 500mg",
  "description": "Pain relief tablets",
  "basicUnitId": "...",
  "basicUnitName": "Tablet",
  "basicUnitPrice": 2.50,
  "packagingUnitId": "...",
  "packagingUnitName": "Box",
  "packagingUnitPrice": 60.00,
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

`packagingUnitId`, `packagingUnitName`, and `packagingUnitPrice` are `null` when no packaging unit is configured.

---

## GET /api/v1/products

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by product name |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `isActive` | `bool` | `true` = active only, `false` = inactive only |
| `vehicleId` | `guid` | Return only products with a stock record for this vehicle |
| `inStockOnly` | `bool` | Requires `vehicleId`. When `true`, require positive basic or packaging stock; default `false` |
| `excludeVehicleStock` | `bool` | Requires `vehicleId`. When `true`, return catalogue products with no stock record for that vehicle; default `false` |

### Vehicle filter behaviour

| Request | Products returned | `vehicleStock` |
|---|---|---|
| No `vehicleId` | All matching catalogue products | `null` |
| `vehicleId={id}` | Products already tracked by the vehicle, including zero-balance rows | Current stock summary |
| `vehicleId={id}&inStockOnly=true` | Tracked products where either basic or packaging quantity is greater than zero | Current stock summary |
| `vehicleId={id}&excludeVehicleStock=true` | Products not yet tracked by the vehicle | `null` |

Use `excludeVehicleStock=true` for the **Add product to vehicle** picker. Use `inStockOnly=true` where the user must select something currently available on the vehicle. Use `vehicleId` alone for catalogue management because it keeps zero-balance products visible.

### Response `200 OK` — `PaginatedData<ProductListResponse>` when paginated

Send both `pageNumber` and `pageSize` for the pagination envelope shown below. If pagination is omitted, the query builder returns the matching product array directly.

Without `vehicleId`, the endpoint behaves as before and `vehicleStock` is `null`. With `vehicleId`, every returned item has its stock summary. A product with a vehicle stock record and zero balances is still returned unless `inStockOnly=true`.

```http
GET /api/v1/products?vehicleId=<vehicle-guid>&inStockOnly=true&isActive=true&pageNumber=1&pageSize=20
```

```json
{
  "totalCount": 1,
  "totalPages": 1,
  "currentPage": 1,
  "pageSize": 20,
  "nextPageUrl": null,
  "previousPageUrl": null,
  "path": "https://api.example.com/api/v1/products?vehicleId=<vehicle-guid>&inStockOnly=true&pageNumber=1&pageSize=20",
  "links": [
    "https://api.example.com/api/v1/products?vehicleId=<vehicle-guid>&inStockOnly=true&pageNumber=1&pageSize=20"
  ],
  "data": [
    {
      "id": "<product-guid>",
      "name": "Paracetamol 500mg",
      "description": "Pain relief tablets",
      "basicUnitId": "<unit-guid>",
      "basicUnitName": "Tablet",
      "basicUnitPrice": 2.5,
      "packagingUnitId": "<unit-guid>",
      "packagingUnitName": "Box",
      "packagingUnitPrice": 60,
      "isActive": true,
      "createdAt": "2026-10-01T10:00:00Z",
      "updatedAt": null,
      "vehicleStock": {
        "stockId": "<stock-guid>",
        "basicQuantityOnHand": 100,
        "packagingQuantityOnHand": 4,
        "lowStockThreshold": 20,
        "isLowStock": false,
        "updatedAt": "2026-10-01T12:00:00Z"
      }
    }
  ]
}
```

`inStockOnly=true` considers the product available when either quantity is greater than zero. It does not use trek stock-load allocations. Existing filters (`search`, `isActive`, sorting and pagination) combine with the vehicle filters.

To populate an “add a new product to this vehicle” picker without losing server-side pagination, request:

```http
GET /api/v1/products?vehicleId=<vehicle-guid>&excludeVehicleStock=true&isActive=true&pageNumber=1&pageSize=20
```

This returns only catalogue products that have no `VehicleProductStock` record for the selected vehicle. Their `vehicleStock` value is `null`. A zero-balance product is still considered part of the vehicle catalogue and is therefore excluded. Search, active-status, sorting and pagination are applied after the exclusion filter.

After a successful stock load creates the vehicle stock record, remove the product from the picker locally or refetch this query. It will no longer be returned by the exclusion filter.

`excludeVehicleStock=true` and `inStockOnly=true` are mutually exclusive: one requests untracked products, while the other requests tracked products with a positive balance.

Errors:

- `400` — `inStockOnly=true` was supplied without `vehicleId`.
- `400` — `excludeVehicleStock=true` was supplied without `vehicleId`, or both stock flags were true.
- `404` — the vehicle does not exist.

---

## POST /api/v1/products

Fetch `GET /api/v1/units` first and present as dropdowns for basic unit and (optionally) packaging unit.

### Request body

```json
{
  "name": "Paracetamol 500mg",
  "description": "Pain relief tablets",
  "basicUnitId": "<unit-guid>",
  "basicUnitPrice": 2.50,
  "packagingUnitId": "<unit-guid>",
  "packagingUnitPrice": 60.00
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 200 chars, must be unique |
| `description` | No | Max 500 chars |
| `basicUnitId` | Yes | Must exist in units table |
| `basicUnitPrice` | Yes | >= 0 |
| `packagingUnitId` | No | Must exist in units table, must differ from `basicUnitId` |
| `packagingUnitPrice` | Conditional | Required when `packagingUnitId` is provided, >= 0 |

New products default to `isActive: true`.

### Response `201 Created` — `ProductResponse`

### Errors
- `422` — validation error, duplicate name, or unit not found

---

## PUT /api/v1/products/{id}

Same fields and rules as POST.

### Response `200 OK` — `ProductResponse`

### Errors
- `404` — product not found
- `422` — validation error, duplicate name, or unit not found

---

## PATCH /api/v1/products/{id}/packaging-unit

Sets or clears the packaging unit on a product without doing a full update.

### Request body

```json
{
  "packagingUnitId": "<unit-guid>",
  "packagingUnitPrice": 60.00
}
```

| Field | Required | Constraints |
|---|---|---|
| `packagingUnitId` | No | Must exist in units table, must differ from the product's `basicUnitId` |
| `packagingUnitPrice` | Conditional | Required when `packagingUnitId` is provided, >= 0 |

Send both fields as `null` (or omit them) to clear the packaging unit.

### Response `200 OK` — `ProductResponse`

### Errors
- `404` — product not found, or packaging unit not found
- `422` — validation error, or packaging unit is the same as the basic unit

---

## PATCH /api/v1/products/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `ProductResponse` with updated `isActive`

### Errors
- `404` — product not found

---

## POST /api/v1/products/import

Bulk-imports products from an Excel file. The user specifies which column header in their file maps to each field — so the file layout is flexible and not fixed.

By default the import is **non-destructive**: existing products are skipped. Set `allowUpdate=true` to upsert instead — existing products will have their units and prices updated from the file.

### Request

`Content-Type: multipart/form-data`

| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file | Yes | `.xlsx` or `.xls` file |
| `productNameColumn` | string | Yes | Exact header text for product names, e.g. `"Product Name"` |
| `basicUnitColumn` | string | Yes | Exact header text for the basic unit name, e.g. `"Unit"` |
| `basicUnitPriceColumn` | string | No | Exact header text for the basic unit price, e.g. `"Unit Price"` — defaults to `0` if omitted |
| `packagingUnitColumn` | string | No | Exact header text for the packaging unit name, e.g. `"Package"` |
| `packagingUnitPriceColumn` | string | No | Exact header text for the packaging unit price, e.g. `"Package Price"` |
| `allowUpdate` | boolean | No | `true` = update existing products with values from the file. Default: `false` (skip duplicates) |

Header matching is **case-insensitive**.

### How the file is processed

1. Row 1 is treated as the header row. The specified column headers are located by text match.
2. Every row from row 2 onwards is processed. Blank product name rows are silently skipped.
3. Rows missing a basic unit name are skipped and added to `skippedNames`.
4. **If a product with the same name already exists** (case-insensitive):
   - `allowUpdate=false` (default) → skipped, name added to `skippedNames`
   - `allowUpdate=true` → updated: `basicUnitId`, `basicUnitPrice`, and (if `packagingUnitColumn` was supplied) `packagingUnitId`/`packagingUnitPrice`
5. **If the same product name appears more than once in the file** → only the first occurrence is imported/updated.
6. **If the unit name does not exist in the Units table** → it is created automatically.
7. If the price cell is blank or non-numeric, it defaults to `0`.
8. All changes are committed in a single database round-trip at the end.

> **Packaging unit when updating:** if `packagingUnitColumn` is not supplied in the request, the existing product's packaging unit is left unchanged. If it is supplied and the cell is blank, the packaging unit is cleared.

### Response `200 OK`

```json
{
  "imported": 38,
  "updated": 4,
  "skipped": 3,
  "unitsCreated": 2,
  "skippedNames": [
    "Amoxicillin 500mg"
  ]
}
```

| Field | Description |
|---|---|
| `imported` | Number of new products created |
| `updated` | Number of existing products updated (only > 0 when `allowUpdate=true`) |
| `skipped` | Number of rows skipped (duplicate when not updating, or missing basic unit) |
| `unitsCreated` | Number of new units auto-created from the file |
| `skippedNames` | Full list of product names that were skipped |

### Errors
- `422` — no file provided, missing form fields, unsupported file type, or a column header was not found

```json
{ "message": "Column 'Unit' was not found in the file header row." }
```

### Usage notes

- Build a two-step UI: first let the user upload the file and read the headers client-side from row 1, then present dropdowns — required ones for product name and basic unit; optional ones for basic unit price, packaging unit, and packaging unit price — pre-populated with the detected headers. Add a checkbox for "Update existing products". Submit the file and the chosen values.
- Show `skippedNames` after import so the user knows which rows were ignored.
- Units created during import are immediately available in `GET /api/v1/units`.
