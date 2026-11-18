# 26 — Product Document Matching

> Matched rows and aggregates now include an optional `basicUnitPrice` extracted from Rate / Price / Unit Price columns. Excel supports `priceColumn`. See [vehicle-stock pricing](28-vehicle-stock-pricing.md) for review and stock-load payloads.

Automatic price selection excludes Amount/Total columns. When multiple recognized price headers exist, it uses the first price column after the product description (or the nearest one before it if none follows). Explicit Excel `priceColumn` mappings override this choice. The endpoint and response fields are unchanged.

## Scope

`POST /api/v1/products/match-files` is a standalone queued extraction and catalogue-matching endpoint. It accepts Excel workbooks, PDFs and photos and returns a polling job. It can optionally create unmatched catalogue products. It does not load vehicle stock or allocate trek stock. No vehicle ID or trek ID is accepted. The endpoint remains unauthenticated.

Only active catalogue products are candidates. Every row has `requiresReview: true`, including exact matches: name matching does not prove that the source quantity or unit is correct.

## Upload

Content type: `multipart/form-data`. Repeat the `files` field for multiple files. Supported extensions: `.xlsx`, `.pdf`, `.jpg`, `.jpeg`, `.png`. File contents are checked against the extension.

Optional `excelMappings` is a JSON array, keyed by the zero-based upload `fileIndex`. Each mapping applies to one workbook; omit `worksheet` to read all worksheets. One mapping per file is supported.

Optional `createUnmatchedProducts` is a boolean form field (`"true"` or `"false"`), defaulting to false. Add an opt-in toggle before upload and append its value to the form. The choice is persisted in the queued job arguments and retained on retry. Invalid boolean values return 422.

When enabled, only `Unmatched` rows with no catalogue candidates are eligible for creation. Weak candidates below the display threshold also prevent creation; inspect warnings on rows that remain unmatched. Similar inactive products prevent creation too. Empty, invalid, header/footer-like names and OCR names below 0.85 confidence require manual review. Native PDF/Excel rows without OCR confidence remain eligible.

The backend rechecks normalized names before insertion and serializes the final duplicate check with catalogue writes. Repeated newly created names within a processing attempt reuse the same ID and remain `AutoCreated`. A retry or later upload reuses an already persisted product as an existing match. Creation happens during processing, so closing the modal does not undo it.

The basic unit is resolved against existing active units, including known aliases. If no unique unit matches, the backend uses an existing Piece/Unit, or creates one shared Unit (reactivating an inactive Unit if necessary). Packaging units are not created. Uploaded quantity and price stay on the review row; uploaded prices are not applied to global catalogue pricing. Newly created products use the catalogue's zero default price until a global price is configured, while the positive uploaded price remains available for vehicle stock loading.

New rows return `matchStatus: "AutoCreated"`, `autoCreated: true`, `suggestedProductId`, and a candidate containing the new product and unit IDs. Its `similarityScore` is 0 because it was created rather than matched to an existing product; display a "Newly created" badge instead of a match percentage. All other rows return `autoCreated: false`. `summary.autoCreated` counts created rows (including repeated references), not unique inserted products. Existing summary counters exclude these rows.

```js
const form = new FormData();
files.forEach(file => form.append("files", file));
form.append("createUnmatchedProducts", String(createUnmatchedProducts)); // toggle value; default false
form.append("excelMappings", JSON.stringify([
  {
    fileIndex: 0,
    worksheet: "Stock",
    headerRow: 1,
    productNameColumn: "Description",
    priceColumn: "Unit Price",
    quantityColumn: "Qty",
    unitColumn: "Unit"
  }
]));

const response = await fetch("/api/v1/products/match-files", {
  method: "POST",
  body: form
});
if (!response.ok) throw new Error("Unable to queue the uploaded documents.");
const job = await response.json();
```

Do not manually set the Content-Type header; the browser supplies the multipart boundary. Supply mappings only for uploaded Excel files. Filenames may repeat; use `fileIndex` as the file identity.

Mappings also support `basicQtyColumn` and `packagingQtyColumn`. Header names are matched case-insensitively. When no explicit column mapping is supplied, the API scans the first 100 rows and auto-detects the best product/quantity header row, including stock headers such as `ON HAND`. `headerRow` is used when an explicit mapping is supplied and must be between 1 and 100. Each field must map to a different column.

An index column such as `#`, `No`, or `S/N` is allowed and ignored when it is not mapped as a product or quantity field. Punctuation, spaces and casing are ignored when recognizing headers.

When a field has no explicit mapping, these headers are recognized after removing spaces/punctuation and ignoring case:

| Field | Recognized headers |
|---|---|
| Product | Product, Products, Product Name, Product Description, Item, Item Name, Description, Item Description |
| Basic-unit price | Rate, Price, Unit Price, Basic Price, Basic Unit Price, Selling Price, Sale Price, Sales Price, Unit Rate, Unit Selling Price, Selling Unit Price, Price Per Unit |
| Quantity | Qty, Quantity, Quantities, Qty Loaded, Qty Units |
| Unit | Unit, Units, UOM, Unit of Measure, Form |
| Basic quantity | Basic Qty, Basic Quantity, Basic Units |
| Packaging quantity | Packaging Qty, Packaging Quantity, Packaging Units, Pack Qty |

## Queue and polling

The upload returns HTTP 202 after the files have been stored and queued:

```json
{
  "jobId": "<job-guid>",
  "status": "Queued",
  "pollUrl": "/api/v1/products/match-files/jobs/<job-guid>",
  "resultUrl": "/api/v1/products/match-files/jobs/<job-guid>/result",
  "retryAfterSeconds": 2
}
```

The POST response is only a queue acknowledgement; it is not the matching result. Save `jobId` in component state and poll the supplied `pollUrl` every `retryAfterSeconds` seconds. Do not upload the files again while the job is `Queued` or `Processing`.

Poll `GET /api/v1/products/match-files/jobs/{jobId}`. Stop polling at `Completed`, `PartiallyCompleted`, or `Failed`. Status responses use `Cache-Control: no-store`, so the frontend should not cache them.

```json
{
  "jobId": "<job-guid>",
  "status": "Processing",
  "totalFiles": 2,
  "processedFiles": 1,
  "currentFileIndex": 1,
  "currentPage": 3,
  "currentFilePages": 10,
  "progressPercentage": 65,
  "message": "Processing file 2 of 2, page 3 of 10.",
  "errorMessage": null,
  "resultUrl": null,
  "createdAt": "2026-11-13T15:30:00Z",
  "startedAt": "2026-11-13T15:30:01Z",
  "completedAt": null
}
```

When `resultUrl` is present, request it to obtain the matching response. `Failed` can still have a `resultUrl` when extraction completed but every submitted file failed; fetch that result so the user can see the individual file errors. If a failed job has no `resultUrl`, display `errorMessage` and allow a new upload.

A result requested before it is ready returns HTTP 409 with the current job status. HTTP 404 means the job ID is unknown or its retained record has expired. Jobs and results expire after 24 hours by default.

Suggested browser flow:

```js
async function waitForMatchJob(accepted, signal) {
  const delayMs = Math.max(1, accepted.retryAfterSeconds ?? 2) * 1000;

  while (true) {
    await new Promise(resolve => setTimeout(resolve, delayMs));
    if (signal?.aborted) throw signal.reason;

    const response = await fetch(accepted.pollUrl, {
      signal,
      cache: "no-store"
    });
    if (!response.ok) throw new Error("Unable to check document progress.");

    const status = await response.json();
    updateProgress(status.progressPercentage, status.message);

    if (["Completed", "PartiallyCompleted", "Failed"].includes(status.status)) {
      if (!status.resultUrl) throw new Error(status.errorMessage ?? status.message);

      const resultResponse = await fetch(status.resultUrl, {
        signal,
        cache: "no-store"
      });
      if (!resultResponse.ok) throw new Error("Unable to retrieve the matching result.");
      return resultResponse.json();
    }
  }
}
```

Cancelling this browser polling loop only stops the browser request. It does not cancel the server-side job.

## Result

`GET /api/v1/products/match-files/jobs/{jobId}/result` returns HTTP 200 with the existing batch shape, even when individual files fail. Always inspect each file's status and errors.

```json
{
  "summary": {
    "totalFiles": 1,
    "failedFiles": 0,
    "partialFiles": 0,
    "totalRows": 1,
    "exact": 1,
    "suggested": 0,
    "ambiguous": 0,
    "unmatched": 0
  },
  "files": [
    {
      "fileIndex": 0,
      "fileName": "stock.xlsx",
      "status": "Processed",
      "errors": [],
      "rows": [
        {
          "page": null,
          "worksheet": "Stock",
          "row": 2,
          "extractedName": "Paracetamol 500mg",
          "priceText": "10.34",
          "basicUnitPrice": 10.34,
          "quantityText": "10",
          "basicQtyText": null,
          "packagingQtyText": null,
          "unitText": "Box",
          "quantity": 10,
          "basicQty": null,
          "packagingQty": 10,
          "quantityObservations": [
            { "quantity": 10, "unit": "Box" }
          ],
          "basicUnitText": "Tablet",
          "packagingUnitText": "Box",
          "ocrConfidence": null,
          "matchStatus": "Exact",
          "suggestedProductId": "<product-guid>",
          "candidates": [
            {
              "productId": "<product-guid>",
              "name": "Paracetamol 500mg",
              "similarityScore": 100,
              "basicUnitId": "<unit-guid>",
              "basicUnitName": "Tablet",
              "packagingUnitId": "<unit-guid>",
              "packagingUnitName": "Box"
            }
          ],
          "requiresReview": true,
          "warnings": []
        }
      ]
    }
  ],
  "aggregatedProducts": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicUnitPrice": 10.34,
      "warnings": [],
      "basicQty": null,
      "packagingQty": 10,
      "requiresReview": true,
      "sourceRows": ["Stock:2"]
    }
  ]
}
```

File statuses: `Processed`, `Partial` (some rows plus errors), `Failed` (errors without rows). A processed file may contain only unmatched rows. Errors contain `code`, `message`, and nullable `page`.

Match statuses:

- `Exact`: one normalized exact catalogue name; a suggested product ID is returned.
- `Suggested`: one leading compatible fuzzy candidate; review required.
- `Ambiguous`: duplicate names, near-tied candidates, or missing strength/form/pack details. No suggested product ID.
- `Unmatched`: no acceptable candidate. No suggested product ID.
- `AutoCreated`: created during this processing attempt; the new product ID and basic unit are returned. Review remains required.

Similarity scores range from 0 to 100 and are not probabilities. OCR confidence ranges from 0 to 1 and is separate; `null` means OCR was not used. Numeric/strength and recognized dosage-form conflicts exclude candidates. This is conservative text matching, not a medicine-equivalence engine.

Each file's `rows` are returned in the default review order: automatically created rows first, then unmatched rows, followed by the remaining rows from the lowest best-candidate similarity score to the highest. Rows with the same priority and score retain their extraction order. The original `page`, `worksheet`, and `row` values remain unchanged, so the frontend can still show the source location or apply another sort. When combining multiple files into one table, apply the same sort across all files.

`priceText` preserves the source value. `basicUnitPrice` is nullable and contains only a valid positive basic-unit selling price. Missing, unreadable, negative, percentage, ambiguous, or zero prices resolve to `null`; inspect row warnings. The frontend must let the user review this value before loading vehicle stock.

## Quantity handling

- Preserve and display original quantity/unit strings.
- Parsed quantities use non-negative numbers with a decimal point. Comma-separated, negative or nonnumeric values remain unresolved with warnings.
- Explicit basic/packaging columns populate their corresponding quantities.
- Generic quantity is mapped for an exact match or a selected suggested product using its catalogue units. Common plural/unit abbreviations are recognized (for example `Box`/`Boxes`, `Tablet`/`Tab`, `Bottle`/`Btl`, `Pack`/`Packs`). A missing or dash-only unit uses the product's basic unit. A `FORM` column is reference-only, so its quantity always becomes `basicQty`. Other unrecognized unit labels retain the number as `basicQty` with a review warning instead of discarding it. Unmatched and ambiguous product rows also retain their numeric quantity as `basicQty` so the reviewer can assign a product without re-entering it. Unit conversions are not guessed.
- A quantity string containing multiple values, such as `32 Strips · 32 Box`, is returned as `quantityObservations` and each value is mapped to the catalogue's basic or packaging unit when possible.
- Unresolved values remain `null`, never silently zero.
- If a product has no packaging unit, a packaging quantity is flagged and left unresolved.
- `aggregatedProducts` contains exact matches and automatically created products. It sums basic and packaging quantities independently across files and includes source row references. Suggested, ambiguous and unmatched rows remain available under `files[].rows` for manual selection. Use each source row's `autoCreated` flag to display creation badges.

## PDF and photo behavior

Text-only PDF pages use PdfPig. A PDF page with a readable product/quantity header also uses native text when all embedded images are above the table (for example a company logo). Scanned pages and mixed pages with images reaching the table region still use whole-page OCR so image-based product data is not silently omitted. OCR pages are sent together in one PDF request, avoiding repeated PDF uploads and page decoding. Photos use OCR directly. Configure the local OCR service for these paths; see [OCR deployment](../../../proh-document-ocr/README.md).

Invoice layouts such as `QTY | DESCRIPTION | RATE | FORM | DISC(%) | DISC | TOTAL` are supported. Quantity may precede the description. Every value in this layout's `QTY` column is returned as `basicQty`; `FORM` is preserved in `unitText` only as reference and never changes it to `packagingQty`. `RATE` supplies the optional basic-unit price under the existing pricing convention. Discount and total columns are ignored. Repeated headers are recognized per page. Item-count summaries, totals, and browser URL/page footers end the product table rather than becoming products.

Rows are reconstructed from word/text-box positions and supported column headers. Photo rows can also be recovered when OCR omits a header by using catalogue units and quantity positions. Complex merged cells, multi-line descriptions and unfamiliar layouts need review.

Page numbers are one-based. Excel rows are worksheet row numbers; PDF/photo rows are reconstructed text-line numbers, not stable document record IDs. The minimum OCR confidence across a line is returned. Low confidence adds a warning.

## Limits and errors

Default limits:

| Limit | Value |
|---|---|
| Files/request | 5 |
| File size | 10 MiB |
| Total file bytes | 20 MiB |
| HTTP request including multipart overhead | 21 MiB |
| PDF pages / Excel worksheets | 20 per file |
| Extracted rows | 1,000 per file |
| Excel columns | 256 |
| Product/line text | 1,000 characters |
| Queued processing deadline | 1,800 seconds |
| OCR HTTP deadline | 120 seconds per OCR request |
| Concurrent document jobs | 1 |
| Candidate count | 5 |
| Minimum fuzzy score / tie margin | 75 / 5 |

Settings are under `DocumentMatching`. Coordinate API limits with OCR service settings. Parsing deadlines are cooperative for in-process Excel/PDF parsing; native OCR inference runs in a terminable worker process. Server/reverse-proxy upload limits may reject a request before the application.

- HTTP 202: files stored and queued successfully.
- HTTP 409: the requested result is not ready; the body contains current job status.
- HTTP 404: job not found or no longer retained.
- HTTP 422: invalid request, malformed mappings, empty batch, or file-count/total-size violation.
- HTTP 413: transport/request-size limit exceeded; server-generated responses may use a different body.
- HTTP 500: the validated upload could not be stored or queued; the user may retry.
- Per-file errors include `UnsupportedFormat`, `InvalidFile`, `FileSize`, `InvalidWorkbook`, `ColumnNotFound`, `AmbiguousColumns`, `WorksheetNotFound`, `PageLimit`, `RowLimit`, `TextLimit`, `NoRows`, `ExtractionFailed`, and `ProcessingTimeout`. OCR failures are explicit: `OcrUnavailable` means the service is not configured or cannot be reached, `OcrServiceUnavailable` means the OCR service returned HTTP 503, `OcrServiceTimeout` means it returned a timeout, `OcrServiceRejected` means it rejected the document, and `OcrServiceError` means another OCR HTTP error. Error codes are for diagnostics; messages are user-facing and do not expose HTTP status codes or internal OCR details. Internal service details are logged by the API.

Cancelling the initial upload stops job creation. Closing the browser after HTTP 202 does not cancel the queued work. A processing timeout can preserve rows already processed as a partial result.

The backend needs persistent storage mounted at `DocumentMatching:JobStoragePath` so queued uploads survive container restarts. PostgreSQL stores job state and final JSON results; Hangfire runs the `document-matching` queue with one worker.

Apply migration `20261113153000_AddDocumentMatchJobs` before using the queued endpoints. Deploy the updated OCR service with the backend because scanned PDFs now use its `/extract-document` operation.

## Frontend follow-up

Let staff review the matched product IDs, quantities, units and warnings. Then call the relevant endpoint:

- Vehicle stock: `POST /api/v1/vehicles/{vehicleId}/stock/load` adds quantities to current balances.
- Trek allocation: `POST /api/v1/treks/{trekId}/stock-loads` replaces allocations for submitted products.

The vehicle-load endpoint currently requires `basicQty > 0`, so packaging-only extracted rows need to be resolved according to that endpoint's contract. Do not silently invent a basic quantity. The matching endpoint does not alter these existing validation rules.
