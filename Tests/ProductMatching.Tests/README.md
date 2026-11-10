# Product matching tests

```sh
dotnet test Tests/ProductMatching.Tests/ProductMatching.Tests.csproj
```

No database, external OCR service, or application startup is required. The HTTP integration test hosts only the new endpoint with a fake catalogue and a test-only authentication handler. It does not run application migrations.

Coverage includes matching conflicts/ambiguity, quantities and unit mapping, Excel extraction, actual in-memory PDF extraction, per-page OCR routing, OCR HTTP contracts, cancellation, authentication and partial file failures.

OCR service tests and deployment instructions are in [the standalone OCR project README](../../../proh-document-ocr/README.md). Real OCR accuracy needs representative documents in addition to synthetic contract tests.
