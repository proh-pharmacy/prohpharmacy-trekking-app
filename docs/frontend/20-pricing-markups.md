# 20 — Pricing Markups

## Overview

Markup rules let you adjust product prices up or down from the base catalog price. Rules are configured at two levels — **region** and **customer** — and are resolved automatically by the backend whenever prices are calculated. The frontend never needs to compute prices itself.

---

## How Price Resolution Works

When a product price is needed (trek stop creation, price sync, offline catalog), the backend resolves it using this priority order:

| Priority | Rule type | Applies when |
|---|---|---|
| 1 | Customer + specific product | Customer has a rule for this exact product |
| 2 | Customer-wide | Customer has a rule with no product (covers all products) |
| 3 | Region + specific product | Region has a rule for this exact product |
| 4 | Region-wide | Region has a rule with no product (covers all products) |
| 5 | Base price | No matching rule — catalog price is used as-is |

The formula: `adjustedPrice = round(basePrice × (1 + markupPercentage / 100), 2)`

A `markupPercentage` of `10` increases the price by 10%. A value of `-5` reduces it by 5%.

### Where this is applied transparently

The following endpoints already return markup-adjusted prices — no frontend changes needed:

| Endpoint | What it returns |
|---|---|
| `POST api/v1/treks/{id}/stops` | Stop product prices are snapshotted with markup applied |
| `POST api/v1/treks/{id}/sync-prices` | Resyncs to current catalog with markup applied |
| `GET api/v1/treks/{id}/price-diff` | `catalogBasicUnitPrice` reflects the adjusted price |
| `GET api/v1/treks/driver/{token}/offline/products` | Product prices include region markup for the trek's region |

> Customer markup is applied at trek stop creation time (when the customer is known). The offline product catalog uses region markup only, since no customer is selected yet.

---

## Regional Markup Rules

Manage markup rules for a region from the Organisation / Regions settings area.

### Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/organisation/regions/{regionId}/markups` | List all rules for a region |
| `PUT` | `api/v1/organisation/regions/{regionId}/markups` | Create or update a rule |
| `DELETE` | `api/v1/organisation/regions/{regionId}/markups/{markupId}` | Remove a rule |

---

### GET /api/v1/organisation/regions/{regionId}/markups

Returns paginated markup rules for the region. The region-wide rule (no `productId`) is always listed first, then product-specific rules sorted by product name. Search filters by product name.

#### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Filter by product name |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: unpaginated if omitted |

#### Response `200 OK` — `PaginatedData<MarkupRuleResponse>`

```json
[
  {
    "id": "...",
    "regionId": "...",
    "regionName": "Greater Accra Region",
    "productId": null,
    "productName": null,
    "markupPercentage": 8.00,
    "createdAt": "2026-09-22T10:00:00Z",
    "updatedAt": null
  },
  {
    "id": "...",
    "regionId": "...",
    "regionName": "Greater Accra Region",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "markupPercentage": 12.50,
    "createdAt": "2026-09-22T10:00:00Z",
    "updatedAt": "2026-09-22T14:30:00Z"
  }
]
```

#### Errors
- `404` — region not found

---

### PUT /api/v1/organisation/regions/{regionId}/markups

Creates or updates a markup rule for the region. Sending the same `productId` (or `null`) again updates the existing rule.

#### Request body

```json
{
  "productId": "<product-guid-or-null>",
  "markupPercentage": 8.00
}
```

| Field | Required | Constraints |
|---|---|---|
| `productId` | No | Omit (or send `null`) for a region-wide rule; send a product GUID for a product-specific rule |
| `markupPercentage` | Yes | Between `-99.99` and `500`. Positive = price increase, negative = reduction |

#### Response `200 OK` — same shape as list item above

#### Errors
- `404` — region or product not found
- `422` — validation error

---

### DELETE /api/v1/organisation/regions/{regionId}/markups/{markupId}

Removes the markup rule. Products covered by this rule revert to the next applicable rule (or base price).

#### Response `204 No Content`

#### Errors
- `404` — rule not found on this region

---

## UI Guidance

### Region markup management

Place this in the region detail view or a dedicated "Pricing" tab on the Regions page.

**The list view should show:**
- A distinct row for the region-wide rule (label it "All Products") if one exists, with an option to add/edit it
- One row per product-specific rule showing the product name and markup %
- A button to add a new rule (opens a form with a product selector + percentage input)
- Edit and delete actions per row

**The form:**
- Product field: searchable dropdown populated from the product catalog. Leave blank for a region-wide rule.
- Markup % field: number input. Show a live preview: *"GHS 10.00 → GHS 10.80"* using a known example price so the user can see the effect.
- Clearly label negative values as price reductions.

### Customer markup management

Place this as a "Pricing" or "Markup Rules" tab on the customer detail page. The UI pattern is identical to region markup — same list, same form — with the product selector and percentage input. Refer to [09-customers.md](./09-customers.md) for the endpoint details.

---

## Practical examples

| Scenario | Rule to create |
|---|---|
| All products in Ashanti are 10% more expensive | Region-wide rule on Ashanti Region: `markupPercentage: 10` |
| Paracetamol 500mg has an extra 5% markup in Greater Accra | Product-specific rule on Greater Accra: `productId: <paracetamol-id>`, `markupPercentage: 5` |
| A wholesale customer gets a 3% discount on all products | Customer-wide rule on that customer: `markupPercentage: -3` |
| A specific customer gets a 2% discount on one product only | Product-specific rule on that customer: `productId: <product-id>`, `markupPercentage: -2` |
