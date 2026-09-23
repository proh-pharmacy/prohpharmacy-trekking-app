# 06 — Password Reset

## Overview

A single page `/auth/reset-password` handles three entry points:

| Entry | URL | Who lands here |
|---|---|---|
| New staff / account created | `/auth/reset-password?email=...` | Welcome email "Set Your Password" button |
| Admin-reset password | `/auth/reset-password?email=...` | Welcome email "Set Your Password" button |
| Forgot password | `/auth/reset-password?email=...` | Staff clicks "Forgot password?" on login |
| Token from email | `/auth/reset-password?token=...` | Staff clicks link in reset email |

The page reads the URL params and renders the correct step automatically.

---

## Endpoints Used

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/auth/forgot-password` | Anonymous | Send reset token email |
| `POST` | `api/v1/auth/reset-password` | Anonymous | Set new password using token |

---

## Step 1 — Request a Reset Email

Triggered on the page when `?email=` is present (pre-filled) or when the user arrives via "Forgot password?" from the login page.

```http
POST /api/v1/auth/forgot-password
Content-Type: application/json

{
  "email": "k.asante@prohpharmacy.com"
}
```

- `200` — always returns success regardless of whether the email exists
- `422` — invalid email format

### Frontend behaviour
- Pre-fill the email field from `?email=` if present
- Show a generic success message after submit: **"If that email is registered, a reset link has been sent."**
- Disable the submit button after the first attempt to prevent spamming

---

## Step 2 — Set New Password

When the user clicks the link in the reset email they are sent to `/auth/reset-password?token=...`. The page reads the token and shows a set-password form.

```http
POST /api/v1/auth/reset-password
Content-Type: application/json

{
  "token": "<token-from-url>",
  "newPassword": "theirNewPassword",
  "confirmNewPassword": "theirNewPassword"
}
```

- `200` — password updated, redirect to `/login` with toast "Password updated — please log in."
- `422` — token expired / invalid, or passwords do not match

### Token expiry
Tokens expire after **1 hour**. On failure show: **"This link has expired or has already been used."** with a button to go back to the email step.

---

## Page Logic (single `/auth/reset-password` route)

```
On load:
  ├── ?token=... present  → show Step 2 (set new password form)
  └── else                → show Step 1 (email form, pre-fill from ?email= if present)

Step 1 submit → POST /auth/forgot-password
  └── Always show success message, do not redirect

Step 2 submit → POST /auth/reset-password
  ├── Success → redirect to /login with success toast
  └── Failure → show error + "Request a new link" button (goes back to Step 1)
```

---

## Implementation Checklist

- [x] *(Backend)* `POST /api/v1/auth/forgot-password`
- [x] *(Backend)* `POST /api/v1/auth/reset-password`
- [x] *(Backend)* Password reset email template
- [x] *(Backend)* Welcome email "Set Your Password" button links to `/auth/reset-password?email=...`
- [ ] `/auth/reset-password` page — reads `?email` and `?token` params
- [ ] Step 1: email form, pre-filled from `?email=`, generic success message after submit
- [ ] Step 2: new password + confirm form, reads `?token` from URL
- [ ] Expired/invalid token error state with "Request a new link" action
- [ ] "Forgot password?" link on the login page → `/auth/reset-password`
- [ ] Redirect to `/login` with success toast on password set
