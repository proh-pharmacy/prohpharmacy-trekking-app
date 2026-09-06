# 05 — User Onboarding (Invitations)

## Overview

Users are accounts that can log into the system. They are either created automatically when a staff member is granted app access (see `04-staff-onboarding.md`) or invited directly as standalone admin/manager accounts. This doc covers the direct invitation flow and the first-time setup flow.

---

## Endpoints Used

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/invitations` | Required | Send an invitation to a new user |
| `POST` | `api/v1/invitations/{id}/resend` | Required | Resend an expired invitation |
| `POST` | `api/v1/invitations/accept` | Anonymous | Accept invitation and set password |
| `POST` | `api/v1/auth/setup` | Anonymous | First-time system setup (creates super admin) |

---

## 1. First-Time System Setup

On a fresh deployment, before any user exists, the backend exposes a one-time setup endpoint. This should only be used once — the frontend should check if setup is needed on first load and redirect to a setup page if so.

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

- `200` — super admin created, system ready
- `422` — setup already completed (system already has users)

### Frontend handling
On app load, if login fails with a specific indicator that no users exist, redirect to `/setup`. After successful setup, redirect to `/login`.

---

## 2. Inviting a User

Admins can invite users directly (without linking to a staff record).

```http
POST /api/v1/invitations
Authorization: Bearer <token>
Content-Type: application/json

{
  "email": "manager@prohpharmacy.com",
  "firstName": "Ama",
  "lastName": "Owusu",
  "roles": ["Manager"]
}
```

- `201` — invitation sent
- `422` — email already has an account, invalid role

### What happens
1. A pending user account is created
2. An invitation email is sent with a link: `{FrontendUrl}/accept-invitation?token=<token>`
3. The link is valid for 48 hours

---

## 3. Resending an Invitation

If the original invitation expired or was lost:

```http
POST /api/v1/invitations/{id}/resend
Authorization: Bearer <token>
```

- `200` — new invitation email sent with a fresh token

---

## 4. Accepting an Invitation

When the invited user clicks the link in their email, the frontend reads `?token=` from the URL and presents a "Set your password" form.

```http
POST /api/v1/invitations/accept
Content-Type: application/json

{
  "token": "<token-from-url>",
  "password": "theirChosenPassword",
  "confirmPassword": "theirChosenPassword"
}
```

- `200` — account activated
- `422` — token invalid, expired, or passwords don't match

After success, redirect to `/login` with a success message: "Your account is ready — please log in."

---

## 5. Pending Invitations UI

Show admins which invitations are pending so they can resend if needed.

```http
GET /api/v1/users
Authorization: Bearer <token>
```

Filter for users with status `Pending` or similar to list unaccepted invitations.

---

## UI Flow

```
Settings → Users & Roles
  ├── Invite User button
  │     └── Modal: email, name, role selection → POST /invitations
  │
  ├── Users list
  │     ├── Active users (manage roles, suspend, etc.)
  │     └── Pending users (resend invitation button)
  │
  └── (First run only) /setup page → POST /auth/setup
```

```
/accept-invitation?token=<token>   (public route)
  └── Set password form → POST /invitations/accept
        └── On success → redirect to /login
```

---

## Implementation Checklist

- [ ] `/setup` page — shown only if no users exist yet
- [ ] Invite user modal (email, name, role multi-select)
- [ ] Pending invitations visible in users list
- [ ] Resend invitation action (per user row)
- [ ] `/accept-invitation` public page
  - [ ] Reads `?token` from URL
  - [ ] Shows set-password form (password + confirm)
  - [ ] Handles expired/invalid token with a clear error and resend prompt
  - [ ] Redirects to `/login` on success
