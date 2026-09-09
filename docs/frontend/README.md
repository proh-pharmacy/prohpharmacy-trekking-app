# Frontend Implementation Plans

Step-by-step guides for implementing each area of the Proh Pharmacy Trekking frontend against the backend API. Work through them in order — each builds on the previous.

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
