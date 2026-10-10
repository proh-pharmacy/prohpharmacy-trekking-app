# Product Document Matching

Status: Implemented; deployment and representative-document validation pending  
Date: 2026-10-08

## Goal

Build an independent endpoint that accepts Excel files, PDFs, and photos, extracts product rows, matches their names against the existing product catalogue, and returns reviewable results to the frontend.

The endpoint has no vehicle or trek context. It does not create or update products, load vehicle stock, or allocate trek stock. The frontend owns reviewing results, resolving matches and quantities, and calling the appropriate existing endpoints.

## Proposed API

```http
POST /api/v1/products/match-files
Authorization: Bearer <token>
Content-Type: multipart/form-data
```

- Accept multiple files under the `files` field.
- Supported formats: `.xlsx`, `.pdf`, `.jpg`, `.jpeg`, and `.png`.
- Do not require a vehicle ID or trek ID.
- Support optional Excel column mappings for product name, quantity, unit, basic quantity, and packaging quantity. Define their multipart representation and per-file/sheet scope during contract implementation.
- Return results grouped by file, with page or worksheet and row references where available.
- Preserve repeated rows; the frontend decides whether to combine them.
- Report individual file failures without discarding results from other files.

## Extraction

### Excel

Use EPPlus, which is already part of the project. The owner plans to arrange the required licence before going live; this feature does not replace the existing Excel library.

- Read product names, quantities, and units using column mappings.
- Preserve original cell text alongside parsed values.
- Flag missing or invalid quantities instead of silently substituting zero.
- Define worksheet selection and header-row handling explicitly.

### PDFs containing text

Use PdfPig to extract words and their positions. Reconstruct product rows so that names remain associated with their quantities and units. Text extraction alone is not sufficient to identify table columns reliably across every layout.

### Photos and scanned PDFs

Run PaddleOCR in a separate, locally hosted Docker service, accessible internally by the .NET backend. No paid external OCR API is required; processing uses the deployment's own compute resources.

- Extract text and table/layout information from photographs and scanned pages.
- Handle PDFs containing a mixture of text and scanned pages without duplicating extracted rows.
- Choose and verify the PDF page-rendering dependency during implementation if required by the selected OCR pipeline.
- Preserve uncertain extraction results and flag them for review.
- Validate performance and accuracy using representative stock sheets, invoices, and photos.

## Product Matching

Load the relevant catalogue data once per request and share it across files.

1. Normalize capitalization, whitespace, and appropriate punctuation while preserving meaningful numbers and units.
2. Find normalized exact-name matches. Multiple catalogue products with the same normalized name remain ambiguous.
3. Use FuzzySharp to rank similar candidates for remaining rows.
4. Check medicine strength, dosage form, and pack-size differences before recommending a candidate. Do not treat conflicting strengths as equivalent because the names have a high similarity score.
5. Return a match status and candidate list rather than forcing a selection.

Statuses:

- `Exact`: a unique normalized exact match.
- `Suggested`: a plausible candidate that requires review.
- `Ambiguous`: multiple plausible candidates or unresolved identifying details.
- `Unmatched`: no acceptable candidate.

Similarity scores measure string similarity; they are not probabilities of correctness. OCR confidence, if available, must be represented separately. Tune thresholds and candidate limits against actual documents and catalogue examples.

## Quantities and Units

- Preserve original quantity and unit text.
- Populate basic or packaging quantities only when the mapping is clear.
- Leave unresolved quantities as `null` and provide warnings.
- Do not invent basic-to-packaging conversions: the application tracks those quantities separately.
- Flag conflicting quantity columns, unreadable values, and uncertain unit interpretations.

## Proposed Response

Finalize DTO names during implementation. The response should contain a request summary and per-file results, including per-file errors and row-level warnings.

Illustrative example; the score and identifiers below are placeholders:

```json
{
  "summary": {
    "totalFiles": 1,
    "failedFiles": 0,
    "totalRows": 1,
    "exact": 0,
    "suggested": 1,
    "ambiguous": 0,
    "unmatched": 0
  },
  "files": [
    {
      "fileIndex": 0,
      "fileName": "stock-photo.jpg",
      "status": "Processed",
      "errors": [],
      "rows": [
        {
          "page": 1,
          "worksheet": null,
          "row": 3,
          "extractedName": "Paracetmol 500mg",
          "quantityText": "10",
          "unitText": "boxes",
          "basicQty": null,
          "packagingQty": null,
          "matchStatus": "Suggested",
          "suggestedProductId": "<existing-product-guid>",
          "candidates": [
            {
              "productId": "<existing-product-guid>",
              "name": "Paracetamol 500mg",
              "similarityScore": 94
            }
          ],
          "requiresReview": true,
          "warnings": [
            "Confirm the product and map boxes to its packaging unit."
          ]
        }
      ]
    }
  ]
}
```

Include catalogue unit IDs/names on candidates so the frontend can resolve quantity mappings. Distinguish invalid requests, failed files, partial extraction, and successfully processed files with unmatched rows.

## Frontend Responsibilities

1. Upload documents and any Excel column mappings.
2. Display original extracted values, candidates, quantities, and warnings.
3. Let staff confirm or change product selections and resolve quantity/unit ambiguities.
4. Decide whether to combine repeated rows.
5. Submit confirmed items to the relevant existing endpoint, for example:
   - `POST /api/v1/vehicles/{vehicleId}/stock/load`
   - `POST /api/v1/treks/{trekId}/stock-loads`

These endpoints have different semantics: vehicle loading adds to balances, while trek stock-load updates replace the specified products' allocations. The matching endpoint does not call either endpoint or prepare context-dependent mutations.

## Proposed Code Structure

```text
Features/Products/MatchProductFiles.cs
Services/ProductMatching/ProductNameMatcher.cs
Services/DocumentExtraction/ExcelProductExtractor.cs
Services/DocumentExtraction/PdfProductExtractor.cs
Services/DocumentExtraction/PaddleOcrClient.cs
../proh-document-ocr/   # Standalone Python application, outside this repository
```

- Follow the existing Carter/MediatR vertical-slice pattern for the endpoint, request, validation, response, and handler.
- Use the project's `Result<T>` and error conventions and declare OpenAPI response schemas.
- Keep extraction and matching reusable and independent of fleet and trekking operations.
- Add an internal OCR service contract, deployment configuration, model setup, and health check.
- No new database entities or migrations are planned for the initial feature.

## Processing Controls

- Validate file content as well as extensions.
- Set explicit file-count, file-size, page-count, extracted-row, and processing-time limits.
- Bound concurrent OCR work and support request cancellation.
- Clean up temporary files on success, failure, and cancellation.
- Keep uploaded documents and extracted identity/business data out of routine logs.
- Start with bounded synchronous processing; confirm acceptable batch sizes using measured OCR timings. If those limits cannot meet the intended workload, define an asynchronous job contract before expanding batch support.

## Implementation Checklist

- [x] Finalize request/response DTOs, Excel mappings, limits, and error semantics.
- [x] Add the authenticated standalone endpoint.
- [x] Implement EPPlus extraction and quantity parsing.
- [x] Implement exact and fuzzy catalogue matching with ambiguity handling.
- [x] Implement PdfPig extraction and row reconstruction.
- [x] Add the local PaddleOCR service and .NET client.
- [x] Support images, scanned PDFs, and mixed text/scanned PDFs.
- [x] Add per-file results, partial failures, source references, and review warnings.
- [x] Add processing limits, timeouts, cancellation, and temporary-file cleanup.
- [ ] Test and evaluate representative documents.
- [x] Add a frontend integration guide under `docs/frontend` and link it from its index.

Implementation details and the final response contract are documented in [the integration guide](../frontend/26-product-document-matching.md). Inactive products are excluded. All matches require review. Photos/scanned pages use PaddleOCR text boxes and shared header-based row reconstruction; complex table-layout model inference is not included in this initial version. OCR deployment instructions are in [the service README](../../../proh-document-ocr/README.md).

## Verification and Acceptance Criteria

- Test unique exact matches, spelling errors, duplicate catalogue names, strength/form conflicts, and unmatched rows.
- Test missing/invalid quantities and ambiguous unit mappings without defaulting them to zero.
- Test multiple files, repeated filenames, repeated product rows, corrupt files, and partial failures.
- Verify Excel, text PDFs, scanned PDFs, mixed PDFs, and photos using representative fixtures.
- Verify unauthorized requests, configured limits, OCR timeouts, and cleanup.
- Confirm that requests do not mutate products, vehicle stock, trek allocations, or ledgers.
- Confirm that returned product IDs exist in the catalogue and successful results remain traceable to their source.

## Details to Resolve During Implementation

- Representative document layouts and expected photo quality.
- Whether inactive catalogue products are excluded or returned with an explicit inactive flag.
- Excel worksheet/header selection and mapping serialization.
- Matching thresholds, candidate limits, and handling of strength/unit abbreviations.
- CPU/memory allocation, OCR model choice, and maximum supported batch size.

## Implementation Order

API contract → Excel extraction and catalogue matching → text PDFs → photo/scanned-PDF OCR → verification and frontend documentation.
