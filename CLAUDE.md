# CLAUDE.md — Proh Pharmacy Trekking Backend

## Project Overview

ASP.NET Core Web API for a field operations platform where trekking vans visit pharmacies across Ghana. Built with Carter, MediatR, FluentValidation, EF Core (PostgreSQL), JWT auth, and Resend for email.

---

## Architecture: Vertical Slice Architecture (VSA)

Every feature lives in `Features/<Module>/`. Each operation (Create, Get, GetList, Update, etc.) is **its own file**. There are no repositories, no service layers, no controllers — each slice owns everything it needs.

### File-per-operation rule

**One operation = one `.cs` file.** Do not combine multiple operations into a single file. Even if two operations are small, keep them separate:

```
Features/Staff/
  CreateStaff.cs
  GetStaff.cs
  GetStaffList.cs
  UpdateStaff.cs
  ChangeStaffStatus.cs
  Entities/
    StaffMember.cs
  Enums/
    EmploymentStatus.cs
```

If a single file is getting long (150+ lines), that is a signal to reconsider the scope, not to merge files.

### Anatomy of a feature slice

Every operation file follows this exact structure — **in this order**:

```csharp
// 1. The feature static class (everything nested inside)
public static class CreateFoo
{
    // 2. Command or Query (MediatR request DTO)
    public class Command : IRequest<Result<FooResponse>> { ... }

    // 3. Response DTO
    public class FooResponse { ... }

    // 4. Validator (FluentValidation)
    public class Validator : AbstractValidator<Command> { ... }

    // 5. Handler (internal sealed — not public)
    internal sealed class Handler : IRequestHandler<Command, Result<FooResponse>>
    {
        // constructor injection
        public async Task<Result<FooResponse>> Handle(...) { ... }

        // optional: internal static ToResponse() mapper
        internal static FooResponse ToResponse(Foo f, ...) => new() { ... };
    }
}

// 6. Carter endpoint — a separate TOP-LEVEL class (not nested), implements ICarterModule
public class CreateFooEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/foos", async (CreateFoo.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/foos/{result.Value.Id}", result.Value);
        })
        .WithTags("Foo")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Foo)
        .WithSummary("...")
        .RequireAuthorization();
    }
}
```

**Critical: the `ICarterModule` class must be a separate top-level class**, not nested inside the static feature class. Carter scans the assembly for `ICarterModule` implementations and cannot find them if they are nested.

### Reusing the ToResponse mapper across slices

When a Get or GetList slice needs to build a response, import the static mapper from the Create slice:

```csharp
using static prohpharmacy_trekking_app.Features.Staff.CreateStaff;

// Then call:
CreateStaff.Handler.ToResponse(staff, branchName, hasAppAccess);
```

---

## Result Pattern

Always return `Result<T>` from handlers. Never throw exceptions for expected failures.

```csharp
// Success
return Result.Success(response);

// Failures
return Result.Failure<T>(Error.ValidationError(validation));
return Result.Failure<T>(Error.CreateNotFoundError("Staff member not found."));
return Result.Failure<T>(Error.Conflict("Employee number already exists."));
return Result.Failure<T>(Error.BadRequest("Cannot assign staff to an inactive branch."));
```

Map results to HTTP responses in the endpoint:

| Result type     | HTTP status                        |
| --------------- | ---------------------------------- |
| ValidationError | `UnprocessableEntity` (422)        |
| NotFound        | `NotFound` (404)                   |
| Conflict        | `UnprocessableEntity` (422)        |
| BadRequest      | `UnprocessableEntity` or `BadRequest` |
| Success (create)| `Created` (201)                    |
| Success (other) | `Ok` (200)                         |

---

## Pagination — always use QueryBuilder + Paginator

**Never write custom pagination logic.** The app has `QueryBuilder<T>` and `Paginator` in `Utilities/`. Always use them for list endpoints.

### Standard list handler pattern

```csharp
var query = _db.Things
    .Include(t => t.RelatedEntity)
    .AsNoTracking();

// Apply filters before handing off to QueryBuilder
if (request.SomeFilter.HasValue)
    query = query.Where(t => t.SomeField == request.SomeFilter.Value);

var result = await new QueryBuilder<Thing>(query)
    .WithSearch(request.Search, nameof(Thing.Name), nameof(Thing.Code))
    .WithSort(request.Sort)          // format: "fieldName_asc" or "fieldName_desc"
    .Paginate(request.PageNumber, request.PageSize)
    .BuildAsync(t => (object)CreateThing.Handler.ToResponse(t, ...));

return Result.Success(result);
```

### Standard list query DTO

```csharp
public class Query : IRequest<Result<object>>
{
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public int? PageNumber { get; set; }
    public int? PageSize { get; set; }
    // domain-specific filters
    public Guid? BranchId { get; set; }
    public string? Status { get; set; }
}
```

### Standard list endpoint

```csharp
app.MapGet("api/v1/things", async (
    ISender sender,
    [FromQuery] string? search,
    [FromQuery] string? sort,
    [FromQuery] int? pageNumber,
    [FromQuery] int? pageSize,
    [FromQuery] Guid? branchId) =>
{
    var result = await sender.Send(new GetThingList.Query { ... });
    return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
})
```

---

## Produces response type — always required

Every endpoint **must** declare `.Produces<T>()` so Scalar shows the correct response schema. Place these calls before `.RequireAuthorization()` or `.AllowAnonymous()`.

| HTTP method / purpose | Required annotations |
|---|---|
| POST (create) | `.Produces<CreateFoo.FooResponse>(201).Produces<Error>(422)` |
| POST (action — assign, sync, etc.) | `.Produces<FooResponse>(200).Produces<Error>(404).Produces<Error>(422)` |
| GET single | `.Produces<CreateFoo.FooResponse>(200).Produces<Error>(404)` |
| GET list (paginated) | `.Produces<Paginator.PaginatedData<CreateFoo.FooResponse>>(200)` |
| GET list (non-paginated) | `.Produces<List<FooResponse>>(200)` |
| PUT / PATCH | `.Produces<FooResponse>(200).Produces<Error>(404).Produces<Error>(422)` |
| DELETE / unassign | `.Produces(204).Produces<Error>(404).Produces<Error>(422)` |

Response types nested inside static feature classes must be fully qualified from the endpoint class (which is outside the static class):

```csharp
// Correct — endpoint class is top-level, so qualify with the feature class name
.Produces<CreateFoo.FooResponse>(201)

// If the file already has `using static CreateFoo;` then just:
.Produces<FooResponse>(201)
```

---

## Entity conventions

```csharp
public class Thing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;   // non-nullable strings default to empty
    public string? Notes { get; set; }                  // nullable only when genuinely optional
    public Guid RelatedId { get; set; }
    public ThingStatus Status { get; set; } = ThingStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public RelatedEntity Related { get; set; } = null!; // navigation properties use null-forgiving
}
```

Enums are always stored as strings in the database:

```csharp
entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
```

---

## AppDbContext

Add DbSets grouped by module with a comment header. Add model config in `OnModelCreating` in the same module grouping order.

```csharp
// Fleet
public DbSet<Vehicle> Vehicles => Set<Vehicle>();
```

After adding new entities, always run:

```bash
dotnet ef migrations add <MigrationName>
```

Migrations apply automatically on startup via `db.Database.MigrateAsync()` in `Program.cs`.

---

## Namespace conventions

```
prohpharmacy_trekking_app.Features.<Module>          // slice files
prohpharmacy_trekking_app.Features.<Module>.Entities // domain entities
prohpharmacy_trekking_app.Features.<Module>.Enums    // enumerations
prohpharmacy_trekking_app.Database                   // AppDbContext
prohpharmacy_trekking_app.Services.Email             // email service + models
prohpharmacy_trekking_app.Utilities                  // QueryBuilder, Paginator
prohpharmacy_trekking_app.Shared                     // Result, Error
prohpharmacy_trekking_app.Extensions                 // SwaggerDoc, JWT config
prohpharmacy_trekking_app.Providers                  // AuthProvider, JWTProvider
```

---

## API docs — Scalar

The API docs UI is **Scalar** (not Swagger UI). Swashbuckle generates the OpenAPI spec under the hood; Scalar renders it. Do not reference "Swagger UI" or suggest opening `/swagger` — the docs are at `/scalar` (or whichever path is configured in `Program.cs`).

Endpoint grouping is defined in `Extensions/SwaggerDoc.cs → SwaggerEndpointDefinitions`. Use the existing constants — do not invent new group strings inline.

```csharp
.WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
```

Current groups: `General`, `Auth`, `Admin`, `Organisation`, `Staff`, `Fleet`, `Trekking`, `Customers`, `Visits`, `Reports`.

---

## Email service

- Interface: `IEmailService` in `Services/Email/EmailServiceExtensions.cs`
- Implementation: `EmailService` (sends via Resend HTTP API)
- Fallback: `NullEmailService` (logs a warning — used in dev when `ResendApiKey` is empty)
- Models: `StaffInvitationEmailModel`, `StaffWelcomeEmailModel` — add a new model here for each new email type
- Templates: `Templates/<TemplateName>.cshtml` — Razor files rendered by `FluentEmail.Razor`

### Adding a new email type

1. Add a model class in `Services/Email/EmailServiceExtensions.cs` (alongside existing models). Include `public string Year { get; } = DateTime.UtcNow.Year.ToString();` — the Razor renderer runs sandboxed without system imports.
2. Add a method to `IEmailService` and both implementations (`EmailService` and `NullEmailService`).
3. Create the Razor template in `Templates/`.

### Email template design rules

Follow the design of `Templates/StaffWelcomeEmail.cshtml` exactly:

- **Brand colour:** `#00bf6f` (green) for headers, buttons, borders, links
- **Warning/alert colour:** `#fef2f2` background, `#b91c1c` text (red tint — no icons, no left border)
- **Container:** 580px max-width, `border:1px solid #e2e8f0`, `border-radius:6px`, white background
- **Outer backdrop:** `#f4f6f8`, `padding:48px 16px`
- **Typography:** `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif`
- **Feature card:** `border:1.5px solid #00bf6f`, `border-radius:4px`
- **CTA button:** `background-color:#00bf6f`, `color:#ffffff`, `border-radius:3px`, `padding:9px 22px`
- Use `@Model.Year` (not `@DateTime.UtcNow.Year`) in templates

**Typography & colour rules — strictly follow these:**

Never use pure black (`#000000`) for any text. The existing templates use a slate/gray palette — stick to it:

| Role | Colour | Usage |
|---|---|---|
| Headings / primary emphasis | `#1e293b` | Dark slate — the closest to black, for `<h1>` and critical values |
| Body text | `#475569` | Medium slate — main paragraphs |
| Labels / secondary text | `#64748b` | Lighter slate — table labels, captions |
| Muted / fine print | `#94a3b8` | Pale slate — footer, sub-captions |
| Brand accent | `#00bf6f` | Green — buttons, borders, links, brand name |
| Alert text | `#b91c1c` | Red — error/warning notices only |
| Alert background | `#fef2f2` | Red tint — alert box background |

For **bold or highlighted text** that needs to stand out, increase `font-weight` (500 or 600) and use `#1e293b` — do not jump to pure black. The contrast comes from weight, not from making the colour darker. Never use `#000` or `#111` anywhere in a template.

**Layout rules — strictly follow these:**

- **No box shadows** on any element whatsoever (`box-shadow` is banned).
- **No SVGs.** Do not use `<svg>` tags, inline SVG icons, or SVG image sources. If an icon or visual element is needed, use plain Unicode characters or styled HTML elements instead.
- **Use `<table>` for all layout** that you would otherwise reach for `display:flex` or `display:grid` for — side-by-side columns, label/value pairs, button rows, footers. CSS grid and flexbox have poor support across email clients.
- **Divs are acceptable** only for single-column block elements (spacers, text blocks) where a table would add no value.

**Do not modify existing templates** — only add new ones.

---

## Code style rules

- **No comments by default.** Only add one when the WHY is non-obvious (a hidden constraint, a workaround, a subtle invariant).
- **No summary docstrings** on classes or methods.
- **No trailing "what I just did" summaries** in responses.
- Enum-to-string comparisons in LINQ: use `.ToString().ToLower() == value.ToLower()` (same pattern as existing code).
- Fire-and-forget emails: use `_ = _email.SendAsync(...)` — do not await them inline so a slow email doesn't delay the HTTP response.
- `CreatedBy` on entities: read from `AuthProvider.GetUserId()`, parse to `Guid?` with null check.

---

## Tech stack quick reference

| Concern          | Library                              |
| ---------------- | ------------------------------------ |
| Routing          | Carter                               |
| CQRS             | MediatR                              |
| Validation       | FluentValidation                     |
| ORM              | EF Core + Npgsql (PostgreSQL)        |
| Auth             | JWT (custom `JWTProvider`)           |
| Password hashing | BCrypt.Net-Next                      |
| Email sending    | FluentEmail.Razor + custom ResendSender |
| Email delivery   | Resend HTTP API                      |
| Pagination       | `Utilities/Paginator` + `QueryBuilder<T>` |
| API docs         | Scalar (OpenAPI spec via Swashbuckle) |
