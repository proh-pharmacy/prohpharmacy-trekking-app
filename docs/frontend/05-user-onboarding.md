# 05 — User Onboarding (Granting App Access)

## Overview

A "user" is an `ApplicationUser` — a login account linked to a staff member. Users are created by granting a staff member app access. There is no separate invitation-accept step; the account is created immediately and the staff member sets their own password via the password reset flow.

---

## Endpoints Used

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/auth/setup` | Anonymous | First-time super admin setup |
| `POST` | `api/v1/invitations` | Required | Grant app access to a staff member |
| `POST` | `api/v1/invitations/resend` | Required | Reset password + resend welcome email |

---

## 1. First-Time System Setup

On a fresh deployment, before any user exists, a one-time setup endpoint creates the super admin account.

```http
POST /api/v1/auth/setup
Content-Type: application/json

{
  "firstName": "Admin",
  "lastName": "User",
  "email": "admin@prohpharmacy.com",
  "password": "securepassword"
}
```

- `200` — super admin created
- `422` — setup already done (users exist)

On app load, redirect to `/setup` if no users exist yet. After success redirect to `/login`.

---

## 2. Grant App Access (Create Account)

A staff member must already exist before they can be given app access. This endpoint creates their login account, assigns roles, and sends them a welcome email.

```http
POST /api/v1/invitations
Authorization: Bearer <token>
Content-Type: application/json

{
  "staffMemberId": "uuid",
  "roleNames": ["Driver"],
  "initialPassword": null
}
```

- `201` — account created, welcome email sent
- `404` — staff member not found
- `422` — staff already has an account, invalid role, or offboarded staff

`initialPassword` is optional. If omitted the backend derives it as `firstnamelastname`. The plain password is returned once in the response — display it to the admin so they can share it as a fallback.

### What happens
1. `ApplicationUser` is created with a hashed temporary password
2. Roles are assigned
3. A welcome email is sent to the staff member's email address
4. The "Set Your Password" button in the email links to `/auth/reset-password?email=...`
5. Staff triggers the forgot-password flow from that page to set their own password

---

## 3. Resend Welcome Email

Resets the password back to the default (`firstnamelastname`) and resends the welcome email.

```http
POST /api/v1/invitations/resend
Authorization: Bearer <token>
Content-Type: application/json

{
  "staffMemberId": "uuid"
}
```

- `200` — password reset, welcome email resent
- `404` — no account found for this staff member

---

## UI Flow

```
Staff detail page → "Grant App Access" action
  └── Modal: role selection (multi-select), optional initial password
        └── POST /api/v1/invitations
              ├── Show plain password to admin (returned once in response)
              └── Staff receives welcome email → clicks "Set Your Password"
                    └── /auth/reset-password?email=... (see 06-password-reset.md)

Staff detail page (account exists) → "Resend Welcome Email" action
  └── POST /api/v1/invitations/resend
        └── Staff receives new welcome email with reset password
```

---

## Implementation Checklist

- [x] *(Backend)* `POST /api/v1/invitations` — creates account, sends welcome email
- [x] *(Backend)* `POST /api/v1/invitations/resend` — resets password, resends email
- [x] *(Backend)* Welcome email "Set Your Password" button → `/auth/reset-password?email=...`
- [ ] `/setup` page — shown only if no users exist yet
- [ ] "Grant App Access" action on staff detail page (role selector modal)
- [ ] Display plain password to admin after account creation
- [ ] "Resend Welcome Email" action on staff detail page (when account exists)
