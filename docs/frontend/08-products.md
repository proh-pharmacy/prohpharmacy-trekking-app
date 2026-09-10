# 08 — Products & Units

## Overview

Units are managed separately as a lookup list. When creating or updating a product, fetch the units list and let the user pick from it — the selected unit name is stored as a string on the product.

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

```json
{
  "id": "...",
  "name": "Paracetamol 500mg",
  "unit": "Strips",
  "description": "Pain relief tablets",
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

---

## POST /api/v1/products

Fetch `GET /api/v1/units` first and use the names as a dropdown. Send the selected unit name as the `unit` field.

### Request body

```json
{
  "name": "Paracetamol 500mg",
  "unit": "Strips",
  "description": "Pain relief tablets"
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 200 chars |
| `unit` | No | Max 80 chars — pick from `GET /api/v1/units` |
| `description` | No | Max 500 chars |

- New products default to `isActive: true`

### Response `201 Created` — `ProductResponse`

### Errors
- `422` — validation error

---

## PUT /api/v1/products/{id}

Same fields and rules as POST.

### Response `200 OK` — `ProductResponse`

### Errors
- `404` — product not found
- `422` — validation error

---

## PATCH /api/v1/products/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `ProductResponse` with updated `isActive`

### Errors
- `404` — product not found

---

## POST /api/v1/products/import

Bulk-creates products from an Excel file. The user specifies which column header in their file maps to the product name and which maps to the unit — so the file layout is flexible and not fixed.

### Request

`Content-Type: multipart/form-data`

| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file | Yes | `.xlsx` or `.xls` file |
| `productNameColumn` | string | Yes | Exact text of the header in row 1 that contains product names, e.g. `"Product Name"` |
| `unitColumn` | string | Yes | Exact text of the header in row 1 that contains units, e.g. `"Unit"` |

The header match is **case-insensitive** — `"product name"`, `"Product Name"`, and `"PRODUCT NAME"` all match the same column.

### How the file is processed

1. Row 1 is treated as the header row. The two specified column headers are located by text match.
2. Every row from row 2 onwards is processed. Blank product name rows are silently skipped.
3. **If a product with the same name already exists in the database** (case-insensitive) → the row is skipped and the name is added to `skippedNames`.
4. **If the same product name appears more than once in the file** → only the first occurrence is imported; subsequent duplicates are also skipped.
5. **If the unit value does not exist in the Units table** → it is created automatically before the product is saved.
6. All inserts are committed in a single database round-trip at the end.

### Response `200 OK`

```json
{
  "imported": 42,
  "skipped": 5,
  "unitsCreated": 3,
  "skippedNames": [
    "Amoxicillin 500mg",
    "Paracetamol 500mg",
    "Ibuprofen 400mg",
    "Metformin 850mg",
    "Omeprazole 20mg"
  ]
}
```

| Field | Description |
|---|---|
| `imported` | Number of new products successfully created |
| `skipped` | Number of rows skipped because the product name already existed |
| `unitsCreated` | Number of new units auto-created from the file |
| `skippedNames` | Full list of product names that were skipped |

### Errors
- `422` — no file provided, missing form fields, unsupported file type (not `.xlsx`/`.xls`), or a specified column header was not found in the file

When a column header is not found the error message names the missing header explicitly:
```json
{ "message": "Column 'Product Name' was not found in the file header row." }
```

### Usage notes

- Build a two-step UI: first let the user upload the file and peek at the headers (you can read them client-side from the first row), then present two dropdowns — one for product name column, one for unit column — pre-populated with the detected headers. Submit the file + the two chosen values.
- Show `skippedNames` to the user after import so they know which rows were ignored and why.
- Units created during import are immediately available in `GET /api/v1/units` for use in future product creation.
