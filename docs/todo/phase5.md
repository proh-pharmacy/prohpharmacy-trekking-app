# Phase 5 — Driver Trek Report

## Goal
A per-trek financial and stock report for the driver — showing total money realised, breakdown by customer/product, and how much physical cash should be on hand. Helps the driver reconcile sales against products delivered.

---

## Context

The following data already exists to power this report:
- `TrekkingTripStopProduct` — products sold, amounts due/paid/balance, payment method
- `TrekkingTripStopReturn` — products returned, refund amounts
- `CustomerLedgerEntry` — debit/credit entries per customer per trek
- `TrekStockLoad` (Phase 4) — what was loaded onto the vehicle
- `SaleInvoice` (Phase 2) — invoice per stop

Depends on: **Phase 2** (invoices) and **Phase 4** (stock loads).

---

## Report structure

### Summary section
| Field | Description |
|---|---|
| Total Sales Value | Sum of `AmountDue` across all stops |
| Total Collected | Sum of `AmtPaid` across all stops |
| Total Outstanding (Credit) | Sum of `Balance` across all stops |
| Total Refunds | Sum of `RefundAmount` from returns |
| Net Cash on Hand | Cash + MobileMoney collected minus cash refunds given |

### Breakdown by customer (per stop)
| Field | Description |
|---|---|
| Customer name | |
| Invoice number | From `SaleInvoice` |
| Products sold | Name, qty, unit price, line total |
| Amount due | |
| Amount paid | |
| Payment method | |
| Balance | |
| Returns | Products returned, refund amount |

### Breakdown by payment method
| Method | Amount Collected |
|---|---|
| Cash | |
| MobileMoney | |
| Credit | |
| Cheque | |
| BankTransfer | |

### Stock reconciliation (requires Phase 4)
| Product | Qty Loaded | Qty Delivered | Qty Returned | Expected Remaining |
|---|---|---|---|---|

---

## New endpoint

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/treks/{trekId}/driver-report` | Full trek financial + stock report |

This is a read-only query endpoint — no new entities needed.

---

## Open questions

- [ ] Should this report be accessible to the driver via the driver token (unauthenticated by staff JWT), or does it require staff login?
- [ ] Should the report be available while the trek is still `InProgress`, or only after `Completed`?
- [ ] Does the driver need a printable/downloadable version, or is on-screen sufficient?
