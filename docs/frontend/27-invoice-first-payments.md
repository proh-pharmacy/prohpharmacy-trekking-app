# Invoice first, then payment and receipt

This guide supersedes the payment instructions in the older delivery, sale-invoice,
and offline-sync guides. Deploy the frontend changes together with this backend
change: the delivery endpoints no longer accept payment amounts or methods.

## Frontend implementation sequence

1. While the trek is `Draft`, add, remove or change customer stops freely. Planned
   stop invoice fields are `null`; do not show invoice/payment actions yet.
2. When the trek changes from `Draft` to `Scheduled`, the backend issues one invoice
   for every stop in the same transaction. Refetch the trek, store each `invoiceId`
   and `invoiceNumber`, and label `Issued` as **Awaiting payment**.
3. A walk-in stop added to a `Scheduled` or `InProgress` trek receives its invoice
   immediately because the trek has already crossed the issuance boundary.
4. On the driver stop screen, display planned/invoiced quantities separately from
   actual deliveries. Save added items or deliveries through the existing goods
   routes, then refresh invoice details. Keep the same invoice selected.
5. Provide a separate **Collect payment** action. Read the latest invoice balance,
   let the user confirm the actual amount and method, and submit one payment.
   No money collected means no payment request; do not submit a zero payment or
   use `Credit` as a collection method.
6. Show the returned **Payment receipt** and offer print/download using frontend
   rendering. Keep **Invoice** and **Receipt** as separate views. Reload the invoice
   to display its new status, balance and payment history.
7. For later instalments, reuse the invoice ID but create a new payment UUID.
   For a retry of the same collection, reuse its original UUID and payload.
8. Refresh the trek stop summary, invoice details, ledger and relevant reports after
   successful mutations. Do not wait for trek completion to update customer debt.

Online driver request order:

```text
Schedule trek (issues planned invoices) / add active walk-in / record delivered quantities
  → GET /api/v1/treks/driver/{token}/invoices/{invoiceId}
  → POST /api/v1/treks/driver/{token}/invoices/{invoiceId}/payments
  → display receipt, then refresh invoice and stop summary
```

A failed payment does not undo a previously successful goods request. Keep the
invoice visible and retry only the collection when appropriate. Disable repeated
submission while a request is pending, and persist its UUID/payload before sending
so a lost response can be retried after refresh or reconnect.

## Lifecycle

Adding a planned customer to a Draft trek creates planning data only. Scheduling the
trek creates a numbered invoice for every stop and its products in the same database
transaction. Walk-in stops added to a Scheduled or InProgress trek receive an invoice
immediately, initially empty if no items have been added. The invoice ID/number stay
the same when products, quantities or prices change.

- `Issued`: awaiting payment, including an empty invoice; paid amount is zero.
- `PartiallyPaid`: some payment received and a balance remains.
- `Paid`: the positive invoice total has been paid.
- `Voided`: cancelled, no payments permitted.

Invoice quantity is the planned quantity until delivery is recorded for that line.
Once recorded, the delivered quantities replace the planned quantities for billing.
Basic and packaging quantities are independent, each multiplied by its price.
Unrecorded products remain on the invoice. Use an explicit delivered quantity of
zero for goods that will not be supplied; omitting a product does not cancel it.

Deliveries and additions never imply payment. `PaymentMethod.Credit` means no
money was collected and is not an accepted payment method on the payment endpoint.

Editing an invoice preserves its payments. Reducing its total below money already
received is rejected. Delivered items cannot be deleted through stop replacement;
use the existing returns workflow. Completed trek items are locked, but outstanding
invoices can still receive payments. An issued invoice's customer cannot be replaced.
Stops/treks with invoices cannot be permanently deleted. Cancelling an unpaid,
undelivered trek voids its invoices; paid or delivered treks cannot be cancelled.

## Read invoices

Existing admin routes:

```text
GET /api/v1/invoices/{invoiceNumber}
GET /api/v1/treks/{trekId}/stops/{stopId}/invoice
```

Driver route:

```text
GET /api/v1/treks/driver/{token}/invoices/{invoiceId}
```

The driver route is scoped to the token's region, consistent with the existing
customer invoice history. A cancelled trek's token cannot use the new invoice or
payment routes.

Invoice responses include planned items before delivery and a `payments` array.
Each line adds `stopProductId`, `basicQuantity`, `packagingQuantity`,
`plannedBasicQuantity`, and `plannedPackagingQuantity`. Display `basicQuantity`
and `packagingQuantity` as the invoiced quantities. The existing `basicQtyDelivered`
and `packagingQtyDelivered` still describe delivery and may be null.

Use invoice-level `totalAmount`, `totalPaid`, and `balance` for payment totals.
Line-level `amtPaid`, `balance`, and `paymentMethod` are legacy fields: they do not
represent new payments and must not be summed by the frontend.

Admin trek, driver trek, and offline trek responses expose these fields per stop:

```json
{
  "invoiceId": "<uuid>",
  "invoiceNumber": "INV-GH00001",
  "invoiceStatus": "Issued",
  "invoiceTotal": 600.00,
  "invoicePaid": 0.00,
  "invoiceBalance": 600.00
}
```

Adding a Draft stop returns `null` invoice fields. Refetch the trek after scheduling
to obtain all invoice identities and summaries. Active/offline `AddWalkInStop` results
include `invoiceId`. The existing customer invoice history
used for returns continues to show delivered quantities only, to avoid returning
goods that were merely planned.

## Update goods and record delivery

Keep using the existing stop-product addition, planned stop update, price override,
and `/record` routes. Their changes automatically refresh the invoice. Driver
`/record` accepts delivery quantities without payment details:

```json
{
  "products": [
    {
      "stopProductId": "<uuid>",
      "basicQtyDelivered": 10,
      "packagingQtyDelivered": 2,
      "notes": "Customer accepted the goods"
    }
  ]
}
```

Omit `amtPaid`, `paymentMethod`, and `balance` from delivery and unplanned-product
requests. Non-null values are rejected, including a zero amount, so older clients
cannot accidentally record a sale without actually recording the payment.
`stopInvoices` is a deprecated delivery input; planned invoice identity is established
when the trek is scheduled and does not depend on a delivery or payment submission.

After saving additions/deliveries, refresh the invoice before collecting payment.
Refresh on HTTP 409 if another request changed the invoice/items concurrently.

## Record a payment

```text
POST /api/v1/invoices/{invoiceId}/payments
POST /api/v1/treks/driver/{token}/invoices/{invoiceId}/payments
```

The admin route requires normal authorization. The driver route uses its token
and only permits invoices belonging to that region.

```json
{
  "clientGeneratedId": "<new UUID for this payment>",
  "amount": 400.00,
  "paymentMethod": "Cash",
  "reference": "Optional transaction reference",
  "recordedAt": "2026-11-08T12:30:00Z"
}
```

`recordedAt` is optional online, must be UTC when supplied, and must not be more than
five minutes in the future. `reference` is optional, maximum 200 characters.
Payment methods are `Cash`, `MobileMoney`, `Cheque`, and `BankTransfer`.
`clientGeneratedId`, `amount` and `paymentMethod` are required. Amounts must be
positive, below 1,000,000,000,000, have no more than two decimal places, and not exceed
the outstanding invoice balance. Split tender is multiple payment requests.

Reuse the same `clientGeneratedId` and payload when retrying. A successful retry
returns the existing payment/receipt and does not collect the money twice.
Reusing an identifier with different invoice, amount, method, reference, or an
explicitly different recorded time is rejected. A new instalment uses a new UUID.

Successful response (HTTP 200):

```json
{
  "paymentId": "<uuid>",
  "clientGeneratedId": "<same UUID>",
  "receiptNumber": "RCP-<32 hex characters>",
  "invoiceId": "<uuid>",
  "invoiceNumber": "INV-GH00001",
  "customerAccountId": "<uuid>",
  "amount": 400.00,
  "paymentMethod": "Cash",
  "reference": "Optional transaction reference",
  "recordedAt": "2026-11-08T12:30:00Z",
  "recordedByStaffId": "<uuid>",
  "invoiceTotalAtPayment": 600.00,
  "balanceAfterPayment": 200.00,
  "isLegacy": false
}
```

This is the payment receipt data to display/print. It is a separate document from
the invoice. Snapshot totals on a receipt remain unchanged after later payments or
invoice edits. Refresh the invoice for its current balance and status. Recorded
payments cannot be edited or deleted by these endpoints.

Retrieve the receipt again:

```text
GET /api/v1/payments/{paymentId}/receipt
GET /api/v1/treks/driver/{token}/payments/{paymentId}/receipt
```

## Errors and retries

Errors use `{ "code": "...", "message": "..." }`. Display `message` to the user;
keep status/code for diagnostics. The HTTP status and body code can differ.

| Result | Frontend handling |
|---|---|
| Payment POST `200` | Save the receipt, including on an idempotent retry; refresh invoice totals. |
| Payment POST `422` | Validation or domain failure, including overpayment, missing invoice, voided invoice or reused UUID with different details. Show the message and retain the form. A body code of `409` can indicate an idempotency conflict. |
| HTTP `409` | Concurrent edit or duplicate payment-key race. Refresh the invoice; retry an uncertain collection only with its original UUID and payload. |
| Receipt/invoice GET `404` | Missing or inaccessible resource; check the selected invoice and driver context. |
| Staff route `401` / `403` | Restore the staff session or show the access failure. Driver routes use the token instead of staff JWT. |
| Network error / timeout / `5xx` | Outcome may be unknown. Preserve the payment UUID and original payload; retry or inspect payment history before recording another collection. |

Never generate a new payment UUID merely because a response was lost. Once the
user intentionally changes an accepted payment's details, do not resubmit it as an
edit: this API has no payment edit/delete endpoint.

## Offline sync

Use the existing sync endpoint, adding the `RecordInvoicePayment` action type:

```json
{
  "type": "RecordInvoicePayment",
  "clientId": "<stable payment UUID>",
  "occurredAt": "2026-11-08T12:30:00Z",
  "payload": {
    "invoiceId": "<invoice UUID>",
    "amount": 400.00,
    "paymentMethod": "Cash",
    "reference": "Optional reference"
  }
}
```

For an offline-created stop, use `stopClientId` in place of `invoiceId`. `stopId`
is also accepted. Stop/item/delivery actions are saved before payment actions, so
the invoice exists even when creation and payment arrive in the same batch.
Payment actions are applied in occurrence order. `clientId` is the payment's
idempotency key. Delivery/unplanned-sale actions must omit payment fields.

The payment action result includes `serverId` (payment ID), `invoiceId`, and
`receiptNumber`. Retries return `AlreadySynced`; validation failures return
`Conflict` with a reason. As with other sync conflicts, refresh/review failed
actions instead of repeatedly generating new payment IDs.

## Ledger, reports and migration

Invoice charges enter the customer ledger when the trek is scheduled (or an active
walk-in invoice is created). Later invoice changes update the charge immediately,
and payment credits enter immediately.
Completing a trek does not regenerate those entries. Existing return approval
credits remain a separate adjustment workflow and do not alter receipt history.

Trek collection reports use invoice/payment totals. Product delivery reports use
proportional attribution of invoice collections/outstanding balances to item
values, with rounding residuals assigned deterministically. Their response identifies
this as `collectionBasis: "ProportionalInvoiceAllocation"`; the spreadsheet export
labels the same calculation. These are analytical allocations, not payments made
against individual products. Collection attribution remains to the invoice's trek.

Migration `20261108075557_InvoiceFirstPayments` creates the payment table and ledger
links, backfills missing stop invoices, and includes planned items in active invoice
totals. Existing invoice identities and paid amounts are retained; completed invoice
totals are retained. Existing payment totals are imported with `isLegacy: true` and
`receiptNumber: null`, because historical per-payment receipt details were not stored.
Do not print an imported aggregate as a newly issued receipt. Its timestamp and
staff attribution are inherited metadata, not verified original collection details.

The migration replaces the old automatic sale/payment ledger summaries with linked
invoice debits and payment credits; manual ledger adjustments and return credits
are retained. It should be reviewed and validated on staging with the frontend
changes before production deployment. Running the API applies pending migrations
through its existing startup migration mechanism.

Follow-up migration `20261108092411_IssueInvoicesWhenTrekIsScheduled` aligns data
with the scheduling boundary. It removes only Draft invoices that have no payment,
return, or recorded delivery and removes their linked ledger charge. Invoice numbers
are not recycled. Draft invoices with financial or delivery activity are retained
for explicit review.

## Frontend handoff checklist

- Replace combined delivery/payment forms with separate goods and collection submissions.
- Remove non-null legacy payment fields from online goods requests and offline goods actions.
- Keep Draft invoice fields nullable; refetch and store invoice identity after scheduling.
- Store active walk-in invoice identity from the stop-creation response.
- Render invoice-level totals and `payments`; never sum legacy product payment fields.
- Support partial payments, split tender and stable retry UUIDs.
- Print receipts from successful payment data; mark `isLegacy` entries as imported history.
- Reconcile older queued collections explicitly before migrating their payloads.
- Validate creation → item change → delivery → partial payment → receipt → final payment
  on staging, including an identical retry and a lost response. Backend migration and
  staging verification are still pending; these docs are not confirmation of deployment.
