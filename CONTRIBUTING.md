# Contributing

## Branching

- Branch off `master` for all work
- Use descriptive branch names: `feat/customer-ledger`, `fix/docker-env`, `chore/update-deps`
- Open a PR against `master` when ready

## Commit Style

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(module): add thing
fix(module): correct thing
chore: update deps
refactor(module): restructure thing
```

Examples:
```
feat(trekking): add driver token delivery recording
fix(docker): source .env file at runtime
chore: add .dockerignore
```

## Architecture — Vertical Slice Architecture (VSA)

Every feature lives in `Features/<Module>/`. **One operation = one file.** Do not combine multiple operations into a single file.

### File structure

```
Features/Staff/
  CreateStaff.cs
  GetStaff.cs
  GetStaffList.cs
  UpdateStaff.cs
  Entities/
    StaffMember.cs
  Enums/
    EmploymentStatus.cs
```

### Anatomy of a feature slice

```csharp
// 1. Static feature class containing everything
public static class CreateFoo
{
    // 2. MediatR request DTO
    public class Command : IRequest<Result<FooResponse>> { }

    // 3. Response DTO
    public class FooResponse { }

    // 4. FluentValidation validator
    public class Validator : AbstractValidator<Command> { }

    // 5. Handler — internal sealed, not public
    internal sealed class Handler : IRequestHandler<Command, Result<FooResponse>>
    {
        public async Task<Result<FooResponse>> Handle(...) { }
    }
}

// 6. Carter endpoint — top-level class, NOT nested inside the static class
public class CreateFooEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) { }
}
```

> The `ICarterModule` class **must** be top-level. Carter scans the assembly and cannot find nested classes.

## Result Pattern

Always return `Result<T>` from handlers. Never throw for expected failures.

```csharp
return Result.Success(response);
return Result.Failure<T>(Error.CreateNotFoundError("Not found."));
return Result.Failure<T>(Error.ValidationError(validation));
return Result.Failure<T>(Error.Conflict("Already exists."));
return Result.Failure<T>(Error.BadRequest("Invalid state."));
```

| Error type | HTTP status |
|---|---|
| ValidationError | 422 Unprocessable Entity |
| NotFound | 404 Not Found |
| Conflict | 422 Unprocessable Entity |
| BadRequest | 400 Bad Request |
| Success (create) | 201 Created |
| Success (other) | 200 OK |

## Pagination

Use `QueryBuilder<T>` and `Paginator` from `Utilities/` for all list endpoints. Never write custom pagination logic.

```csharp
var result = await new QueryBuilder<Thing>(query)
    .WithSearch(request.Search, nameof(Thing.Name))
    .WithSort(request.Sort)
    .Paginate(request.PageNumber, request.PageSize)
    .BuildAsync(t => (object)CreateThing.Handler.ToResponse(t));
```

## Entity Conventions

- `Id` defaults to `Guid.NewGuid()`
- Non-nullable strings default to `string.Empty`
- Enums stored as strings: `.HasConversion<string>().HasMaxLength(30).IsRequired()`
- Navigation properties use `null!`
- Always add a migration after changing entities: `dotnet ef migrations add <Name>`

## Code Style

- No comments by default — only when the WHY is non-obvious
- No summary docstrings
- Fire-and-forget emails: `_ = _email.SendAsync(...)`
- `CreatedBy` on entities: read from `AuthProvider.GetUserId()`, parse to `Guid`
