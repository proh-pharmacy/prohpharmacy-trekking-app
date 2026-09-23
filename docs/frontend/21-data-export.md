# 21 — Data Export

## Overview

The **Data Export** section provides five download-only endpoints that package operational data as `.xlsx` files. These are distinct from the Reports section — they export raw entity data (customers, products, staff, markup rules) rather than aggregated analytics. All endpoints require authentication and respond with a file attachment.

---

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/customers/export` | Export customers grouped by region (one sheet per region) |
| `GET` | `api/v1/products/export` | Export product catalog or regional pricing workbook |
| `GET` | `api/v1/staff/export` | Export staff list |
| `GET` | `api/v1/organisation/markups/export` | Export regional markup rules |
| `GET` | `api/v1/customers/markups/export` | Export customer-specific markup rules |

---

## Reusable download helper

All five endpoints follow the same fetch pattern — Bearer token in the header, filename read from `Content-Disposition`:

```js
async function downloadExport(url, fallbackName, token) {
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!response.ok) throw new Error(await response.text());
  const disposition = response.headers.get('Content-Disposition');
  const filename = disposition?.match(/filename="?([^";]+)"?/)?.[1] ?? fallbackName;
  const blob = await response.blob();
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}
```

---

## GET /api/v1/customers/export

Downloads a `.xlsx` workbook with **one sheet per region**. Each sheet lists customers in that region ordered by business name.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string?` | Filter by business name, customer code, or primary phone |
| `regionId` | `guid?` | Limit to a single region (workbook will have one sheet) |
| `branchId` | `guid?` | Filter by owning branch |
| `customerType` | `string?` | `Retail`, `Wholesale`, `Distributor`, `Other` |
| `status` | `string?` | `Pending`, `Active`, `Suspended`, `Inactive` |

### Columns per sheet

| Column | Description |
|---|---|
| Customer Code | Unique customer identifier |
| Business Name | Registered business name |
| Trading Name | Trading/commonly known name (may be empty) |
| Customer Type | `Retail`, `Wholesale`, etc. |
| District | Primary location district |
| Primary Phone | Account-level phone number |
| WhatsApp | WhatsApp number (may be empty) |
| Representative | Primary contact full name |
| Rep Phone | Primary contact phone number |
| Status | Registration status |

### Usage

```js
downloadExport('/api/v1/customers/export', 'customers.xlsx', token);

// With filters:
downloadExport(
  '/api/v1/customers/export?regionId=<guid>&status=Active',
  'customers.xlsx',
  token
);
```

---

## GET /api/v1/products/export

Downloads a `.xlsx` product workbook in one of two modes controlled by the `mode` query parameter.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `mode` | `string?` | `catalog` (default) or `pricing` |

### `catalog` mode

Single sheet. One row per product with base pricing only.

| Column | Description |
|---|---|
| # | Row number |
| Product Name | Full product name |
| Basic Unit | e.g. Tablet, Bottle, Vial |
| Basic Price (GHS) | Base unit price before any markup |
| Packaging Unit | e.g. Box, Carton (blank if none) |
| Packaging Price (GHS) | Base packaging unit price (blank if none) |
| Active | `Yes` / `No` |

### `pricing` mode

Two sheets in the same workbook. Both use the same markup resolution logic: product-specific markup takes precedence over region-wide markup; base price shown if no markup rule exists. Region column headers include the region-wide markup percentage where one is configured.

**Sheet 1 — Basic Unit Pricing.** One row per product.

| Column | Description |
|---|---|
| Product Name | Full product name |
| Basic Unit | Unit of measure |
| Basic Price | Base price before markup |
| `<Region Name>` × N | One column per region — resolved basic unit price in GHS |

**Sheet 2 — Packaging Unit Pricing.** Only products that have a packaging unit. Omitted entirely if no products have a packaging unit.

| Column | Description |
|---|---|
| Product Name | Full product name |
| Packaging Unit | e.g. Box, Carton |
| Packaging Price | Base packaging price before markup |
| `<Region Name>` × N | One column per region — resolved packaging unit price in GHS |

### Usage

```js
// Catalog export (default)
downloadExport('/api/v1/products/export', 'products_catalog.xlsx', token);

// Regional pricing export
downloadExport('/api/v1/products/export?mode=pricing', 'products_pricing.xlsx', token);
```

---

## GET /api/v1/staff/export

Downloads a single-sheet `.xlsx` staff list ordered by branch name, then last name.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string?` | Filter by first name, last name, email, or employee number |
| `branchId` | `guid?` | Filter by branch |
| `status` | `string?` | `Active`, `Inactive`, `OnLeave`, `Terminated` |

### Columns

| Column | Description |
|---|---|
| Employee Number | Staff employee number |
| First Name | |
| Last Name | |
| Email | |
| Phone | |
| Role | System role(s) from app access; falls back to staff role string if no app account |
| Branch | Assigned branch |
| Employment Status | e.g. `Active`, `Terminated` |
| Joined On | Date joined in `YYYY-MM-DD` format |
| App Access | `Yes` if the staff member has an app login, `No` otherwise |

### Usage

```js
downloadExport('/api/v1/staff/export', 'staff.xlsx', token);

// Active staff for a specific branch:
downloadExport(
  `/api/v1/staff/export?branchId=<guid>&status=Active`,
  'staff.xlsx',
  token
);
```

---

## GET /api/v1/organisation/markups/export

Downloads a single-sheet `.xlsx` of all regional markup rules. Ordered by region name; the region-wide rule (all products) is listed before product-specific rules for each region.

### No query parameters — exports all rules.

### Columns

| Column | Description |
|---|---|
| Region | Region name |
| Product | Product name, or `All Products` for a region-wide rule |
| Markup % | Markup percentage applied on top of the base price |

### Usage

```js
downloadExport('/api/v1/organisation/markups/export', 'regional_markup_rules.xlsx', token);
```

---

## GET /api/v1/customers/markups/export

Downloads a single-sheet `.xlsx` of all customer-specific markup rules. A customer with both a general (all-products) rule and product-specific rules will appear on multiple rows. Ordered by region, then customer name, with the general rule listed first.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `regionId` | `guid?` | Filter to a single region |

### Columns

| Column | Description |
|---|---|
| Customer Code | Unique customer identifier |
| Customer Name | Business name |
| Region | Customer's region |
| Product | Product name, or `All Products` for a customer-wide rule |
| Markup % | Markup percentage |

### Usage

```js
downloadExport('/api/v1/customers/markups/export', 'customer_markup_rules.xlsx', token);

// Filtered by region:
downloadExport(
  `/api/v1/customers/markups/export?regionId=<guid>`,
  'customer_markup_rules.xlsx',
  token
);
```
