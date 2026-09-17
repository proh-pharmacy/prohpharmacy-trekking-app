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
| `GET` | `api/v1/products` | List all products (paginated, searchable) |
| `POST` | `api/v1/products` | Create a product |
| `POST` | `api/v1/products/import` | Bulk import products from Excel |
| `PUT` | `api/v1/products/{id}` | Update a product |
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

### Response `200 OK` — `PaginatedData<ProductResponse>`

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

## PATCH /api/v1/products/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `ProductResponse` with updated `isActive`

### Errors
- `404` — product not found

---

## POST /api/v1/products/import

Bulk-creates products from an Excel file. The user specifies which column header in their file maps to each field — so the file layout is flexible and not fixed. This operation is **non-destructive**: existing products are never modified or deleted.

### Request

`Content-Type: multipart/form-data`

| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file | Yes | `.xlsx` or `.xls` file |
| `productNameColumn` | string | Yes | Exact header text for product names, e.g. `"Product Name"` |
| `basicUnitColumn` | string | Yes | Exact header text for the basic unit name, e.g. `"Unit"` |
| `basicUnitPriceColumn` | string | No | Exact header text for the basic unit price, e.g. `"Unit Price"` — defaults to `0` if omitted |
| `packagingUnitColumn` | string | No | Exact header text for the packaging unit name, e.g. `"Package"` |
| `packagingUnitPriceColumn` | string | No | Exact header text for the packaging unit price, e.g. `"Package Price"` — defaults to `0` if omitted |

Header matching is **case-insensitive**.

### How the file is processed

1. Row 1 is treated as the header row. The specified column headers are located by text match.
2. Every row from row 2 onwards is processed. Blank product name rows are silently skipped.
3. Rows missing a basic unit name are skipped and added to `skippedNames`.
4. **If a product with the same name already exists** (case-insensitive) → skipped, name added to `skippedNames`.
5. **If the same product name appears more than once in the file** → only the first occurrence is imported.
6. **If the unit name does not exist in the Units table** → it is created automatically.
7. If the price cell is blank or non-numeric, it defaults to `0`.
8. All inserts are committed in a single database round-trip at the end.

### Response `200 OK`

```json
{
  "imported": 42,
  "skipped": 5,
  "unitsCreated": 3,
  "skippedNames": [
    "Amoxicillin 500mg",
    "Paracetamol 500mg"
  ]
}
```

| Field | Description |
|---|---|
| `imported` | Number of new products successfully created |
| `skipped` | Number of rows skipped (duplicate name or missing basic unit) |
| `unitsCreated` | Number of new units auto-created from the file |
| `skippedNames` | Full list of product names that were skipped |

### Errors
- `422` — no file provided, missing form fields, unsupported file type, or a column header was not found

```json
{ "message": "Column 'Unit' was not found in the file header row." }
```

### Usage notes

- Build a two-step UI: first let the user upload the file and read the headers client-side from row 1, then present dropdowns — required ones for product name, basic unit, and basic unit price; optional ones for packaging unit and packaging unit price — pre-populated with the detected headers. Submit the file and the chosen values.
- Show `skippedNames` after import so the user knows which rows were ignored.
- Units created during import are immediately available in `GET /api/v1/units`.
