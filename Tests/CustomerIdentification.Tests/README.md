# Customer identification tests

Run against an isolated PostgreSQL instance (not the application database). Set `CUSTOMER_ID_TEST_POSTGRES` to a connection string for a test role with database creation permission, then run:

```sh
dotnet test Tests/CustomerIdentification.Tests/CustomerIdentification.Tests.csproj
```

The integration test creates and deletes its own randomly named `trek_id_sync_*` database. It never applies application migrations or clears the database named in the supplied connection string. ImageKit HTTP responses are stubbed; no files are uploaded to the external service.

Coverage: metadata validation, same-batch registration/update and duplicate detection, registration replay, previous-batch client-ID resolution, omission semantics, rejection before mutation, region/token restrictions, image validation and side-specific replacement, and customer GET/delta-seed read-back.

To run only metadata parsing tests without PostgreSQL:

```sh
dotnet test Tests/CustomerIdentification.Tests/CustomerIdentification.Tests.csproj --filter Metadata_requires
```
