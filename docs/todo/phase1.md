# Phase 1 — Customer / Institution Enrichment

## Goal
Capture richer institution data: the institution's identifying document (type + number + front/back photos). The document type, number, and photos are one unified concept — the type tells you what the number is, and the photos are visual proof of that same document.

---

## Changes to `CustomerAccount` entity

### New fields

| Field | Type | Notes |
|---|---|---|
| `IdDocumentType` | `CustomerIdDocumentType` (enum) | What kind of document |
| `IdDocumentNumber` | `string?` | The number on the document — e.g. pharmacy licence number, Ghana card number. **Unique across the system (database constraint).** |
| `IdCardFrontUrl` | `string?` | ImageKit URL — front of the document |
| `IdCardBackUrl` | `string?` | ImageKit URL — back of the document (independently optional) |

### How they relate

```
IdDocumentType    → what it is (PharmacyLicence, GhanaCard, etc.)
IdDocumentNumber  → the number on it (searchable, referenceable)
IdCardFrontUrl    → visual proof (front)
IdCardBackUrl     → visual proof (back)
```

Example — pharmacy: `IdDocumentType = PharmacyLicence`, `IdDocumentNumber = "GH-PHARM-00234"`, photos of that licence card.

Example — Ghana Card: `IdDocumentType = GhanaCard`, `IdDocumentNumber = "GHA-XXXXXX-X"`, front and back photos.

### Document requirement by `CustomerType`

Validation applies when moving out of `Draft` status (not enforced on initial registration).

| CustomerType | IdDocumentType + IdDocumentNumber |
|---|---|
| RetailPharmacy | Required |
| WholesalePharmacy | Required |
| LicensedHealthFacility | Required |
| OTCMedicineSeller | Required |
| Clinic | TBD — needs client confirmation |
| Hospital | TBD — needs client confirmation |
| ChemicalShop | Optional |
| Other | Optional |

Photos (`IdCardFrontUrl`, `IdCardBackUrl`) are always optional regardless of customer type.

---

## New enum: `CustomerIdDocumentType`

```
GhanaCard
PharmacyLicence
BusinessRegistration
DriversLicence
Passport
Other
```

Location: `Features/Customers/Enums/CustomerIdDocumentType.cs`

---

## New endpoints needed

All follow the same pattern as the existing `POST /api/v1/customers/{id}/premises-photo` endpoint — dedicated attachment endpoints, not part of `CreateCustomer` or `UpdateCustomer`.

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/v1/customers/{id}/id-document` | Set/update `IdDocumentType` + `IdDocumentNumber` |
| `POST` | `/api/v1/customers/{id}/id-card/front` | Upload front photo (ImageKit) |
| `POST` | `/api/v1/customers/{id}/id-card/back` | Upload back photo (ImageKit) |

---

## Migration

New migration: `AddCustomerIdDocument`

---

## Open questions

- [ ] Are `Clinic` and `Hospital` required to have a document number?
- [x] `IdDocumentNumber` enforces a unique constraint at the database level.
