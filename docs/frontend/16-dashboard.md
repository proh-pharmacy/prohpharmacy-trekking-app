# 16 — Dashboard

## Overview

The dashboard is powered by **three parallel API calls**. Fire them all at once with `Promise.all` — the page loads as fast as the slowest of the three.

| Call | Endpoint | Purpose |
|---|---|---|
| 1 | `GET /api/v1/dashboard` | KPI cards + bar chart data |
| 2 | `GET /api/v1/ledger/summary?pageSize=5&sort=balance_desc&hasBalance=true` | Top 5 debtors widget |
| 3 | `GET /api/v1/reports/treks?pageSize=5` | Recent treks widget |

---

## GET /api/v1/dashboard

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `period` | `string?` | `week` (default) or `month` — drives the time-series chart and `totalCollected` |
| `branchId` | `guid?` | Filter all data to a single branch |

### Response `200 OK`

```json
{
  "period": "week",
  "totalCollected": 18500.00,
  "totalOutstanding": 42000.00,
  "activeTreks": 4,
  "customersWithDebt": 27,
  "collectionsOverTime": [
    { "label": "Mon", "date": "2026-09-07", "total": 3200.00 },
    { "label": "Tue", "date": "2026-09-08", "total": 5100.00 },
    { "label": "Wed", "date": "2026-09-09", "total": 4800.00 },
    { "label": "Thu", "date": "2026-09-10", "total": 5400.00 },
    { "label": "Fri", "date": "2026-09-11", "total": 0.00 },
    { "label": "Sat", "date": "2026-09-12", "total": 0.00 },
    { "label": "Sun", "date": "2026-09-13", "total": 0.00 }
  ],
  "byPaymentMethod": [
    { "paymentMethod": "Cash", "total": 9500.00, "transactions": 32 },
    { "paymentMethod": "MobileMoney", "total": 6200.00, "transactions": 21 },
    { "paymentMethod": "Cheque", "total": 2800.00, "transactions": 8 }
  ]
}
```

### Field notes

| Field | Scope | Notes |
|---|---|---|
| `totalCollected` | Period | Sum of all Credit ledger entries within the selected week or month |
| `totalOutstanding` | All-time | Sum of positive customer balances — current debt state, not period-filtered |
| `activeTreks` | Current | Count of treks with status `Scheduled` or `InProgress` |
| `customersWithDebt` | All-time | Count of customers where `totalDebits > totalCredits` |
| `collectionsOverTime` | Period | One entry per day in the period, zero-filled — always 7 items for `week`, 28–31 for `month` |
| `byPaymentMethod` | Period | Breakdown of credit entries by payment method, sorted by total desc. `"Unspecified"` appears when a credit entry has no payment method recorded |

### Period behaviour

| `period` | Date range | `label` format |
|---|---|---|
| `week` | Monday → Sunday of the current calendar week | `"Mon"`, `"Tue"`, … `"Sun"` |
| `month` | 1st → last day of the current calendar month | `"1"`, `"2"`, … `"30"` |

All dates are UTC.

---

## Wiring it up

```js
const [dashboard, debtors, recentTreks] = await Promise.all([
  fetch('/api/v1/dashboard?period=week', { headers: { Authorization: `Bearer ${token}` } }).then(r => r.json()),
  fetch('/api/v1/ledger/summary?pageSize=5&sort=balance_desc&hasBalance=true', { headers: { Authorization: `Bearer ${token}` } }).then(r => r.json()),
  fetch('/api/v1/reports/treks?pageSize=5', { headers: { Authorization: `Bearer ${token}` } }).then(r => r.json()),
]);
```

### KPI cards

| Card | Field | Notes |
|---|---|---|
| Total Collected | `dashboard.totalCollected` | Label with period: "This Week" or "This Month" |
| Total Outstanding | `dashboard.totalOutstanding` | Always all-time — label as "Outstanding (All Time)" |
| Active Treks | `dashboard.activeTreks` | Scheduled + InProgress |
| Customers with Debt | `dashboard.customersWithDebt` | Tap to navigate to `/ledger/summary?hasBalance=true` |

### Collections Over Time bar chart

Use `dashboard.collectionsOverTime` directly — `label` is the X-axis, `total` is the bar height. The array is always fully populated (zero values for days with no collections), so no gap-filling needed on the frontend.

Switch between `week` and `month` by re-fetching with the new `period` param.

### Collections by Payment Method bar chart

Use `dashboard.byPaymentMethod` — `paymentMethod` is the X-axis category, `total` is the bar height. Show `transactions` as a subtitle or tooltip on each bar.

### Top 5 Debtors widget

Use `debtors.customers` (from the ledger summary response — see doc 14). Show business name, customer code, and `currentBalance`. Tap a row to navigate to the customer's ledger page.

### Recent Treks widget

Use `recentTreks.treks` (from the trek report response — see doc 15). Show trek number, scheduled date, driver name, and status badge.
