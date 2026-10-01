# Frontend Implementation Plans

Implementation guides for the Proh Pharmacy Trekking frontend against the backend API. Start with conventions, then follow the relevant feature guides.

The Phase 1–5 changes were reviewed against the code and the 2026-10-01 commits `8b5ca3d` and `aa7f3d8`. These guides describe implemented routes and response DTOs; `docs/todo` contains design notes that sometimes differ from the implementation. Known integration gaps are called out in the affected guides.

## Guides

| # | Guide | Description |
|---|---|---|
| 00 | [API Conventions & Error Handling](./00-api-conventions.md) | Error response shape, status codes, pagination, auth errors |
| 01 | [Login Implementation](./01-login-implementation.md) | Auth flow, token storage, silent refresh, protected routes |
| 02 | [Initial Setup](./02-initial-setup.md) | Regions, districts, and branches configuration |
| 03 | [Roles & Permissions](./03-roles-and-permissions.md) | Role assignment, permission guards, user status management |
| 04 | [Staff Onboarding](./04-staff-onboarding.md) | Creating staff, granting app access, invitation acceptance |
| 05 | [User Onboarding](./05-user-onboarding.md) | Direct invitations, first-time setup, accepting invitations |
| 06 | [Password Reset](./06-password-reset.md) | Forgot password flow, reset token, set new password |
| 07 | [Organisation](./07-organisation.md) | Regions, districts, branches |
| 08 | [Products](./08-products.md) | Product catalogue — create, list, update, toggle status |
| 09 | [Customers](./09-customers.md) | Customer registration, locations, representative portrait |
| 10 | [Fleet](./10-fleet.md) | Vehicles, GPS devices, Traccar drivers and sync |
| 11 | [Trekking](./11-trekking.md) | Trek scheduling, stops, driver link, delivery recording, PDF sheet |
| 12 | [Real-Time Tracking](./12-tracking.md) | Live map with Leaflet + SignalR, position history, webhook setup |
| 13 | [Trek Operations](./13-trek-operations.md) | Day-to-day trek execution — driver portal, delivery recording, ledger sync, PDF sheet |
| 14 | [Ledger](./14-ledger.md) | Customer balances and payment entries |
| 15 | [Reports](./15-reports.md) | Staff reporting and exports |
| 16 | [Dashboard](./16-dashboard.md) | Summary cards and operational statistics |
| 17 | [Search](./17-search.md) | Global search |
| 18 | [Driver Control Panel & Offline Sync](./18-offline-sync.md) | Driver session, local data and action synchronization |
| 19 | [Live Tracking](./19-live-tracking.md) | Live tracking implementation |
| 20 | [Pricing & Markups](./20-pricing-markups.md) | Customer and region pricing overrides |
| 21 | [Data Export](./21-data-export.md) | Export endpoints and downloads |
| 22 | [Sale Invoices](./22-sale-invoices.md) | Invoice issuance, scoped numbering, lookup, delivery sync and printing |
| 23 | [Invoice Returns & Approval](./23-invoice-returns.md) | Online invoice returns, review decisions and ledger effects |
| 24 | [Vehicle Warehouse & Trek Stock Loads](./24-vehicle-warehouse.md) | Bulk restocking/removal, stock ledger, allocations and warnings |
| 25 | [Driver Trek Report](./25-driver-trek-report.md) | Live financial/stock reconciliation and PDF download |

## Phase implementation map

| Phase | Frontend guides |
|---|---|
| 1 — Customer documents | [Customers](./09-customers.md) |
| 2 — Invoices | [Sale invoices](./22-sale-invoices.md), [trek operations](./13-trek-operations.md), [offline sync](./18-offline-sync.md) |
| 3 — Returns approval | [Invoice returns](./23-invoice-returns.md), [ledger](./14-ledger.md) |
| 4 — Vehicle warehouse | [Vehicle warehouse](./24-vehicle-warehouse.md), [fleet](./10-fleet.md) |
| 5 — Driver report | [Driver trek report](./25-driver-trek-report.md), [reports](./15-reports.md) |
