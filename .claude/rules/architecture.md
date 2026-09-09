---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
  - "**/*.slnx"
  - "*.slnx"
---

# Layering, endpoints and the capability model

The shape a PropFlow change has to fit. `docs/architecture.md` is the normative prose; this is
the part that decides a diff. Citations verified 2026-09-09.

## The layer graph

```
Domain  ←  Application  ←  Infrastructure  ←  { Api, Admin }
```

Enforced today by the project references, and it must stay that way:

- `src/PropFlow.Domain/PropFlow.Domain.csproj` has **zero** `ProjectReference` and zero
  `PackageReference`. Do not add one.
- `src/PropFlow.Application/PropFlow.Application.csproj:3` references only Domain.
- `src/PropFlow.Infrastructure/PropFlow.Infrastructure.csproj:4` references Application; EF,
  Npgsql and ASP.NET Identity enter here and nowhere above.
- `src/PropFlow.Api/PropFlow.Api.csproj:3` and `tools/PropFlow.Admin/PropFlow.Admin.csproj:4`
  reference Infrastructure.

An interface that Application needs and Infrastructure implements goes in Application
(`ITenantContext.cs`, `Work/IWorkOperations.cs`, `Communications/IOutbox.cs`).

## Minimal APIs only — the endpoint shape

There are no controllers. Each area is one file in `src/PropFlow.Api/`:

1. `public static class <Area>Endpoints` with a single
   `public static void Map<Area>Endpoints(this WebApplication app)`
   (`WorkEndpoints.cs:8,10` and the five siblings).
2. Registered from `src/PropFlow.Api/Program.cs:116-121`. Adding an area means appending one line there — a
   shared merge point, see `workflow.md`.
3. Request/response types are `sealed record`s at the **bottom of the same file**
   (`WorkEndpoints.cs:55-63`), with a `ToCommand(Guid actor)` mapper on the request
   (`WorkEndpoints.cs:61,63`) so HTTP types never reach the Application layer. The actor comes
   from the principal, never from the body.

## Control flow: outcomes, not exceptions

The Application contract returns enums — `AssignmentOutcome` and `WorkWriteOutcome`
(`IWorkOperations.cs:6-7`) — and the endpoint maps them to status codes
(`WorkEndpoints.cs:31,46`). Do not signal a not-found or a conflict by throwing.

Validation is two tiers:

- **Shape**, in the endpoint, before the call: empty-Guid and range checks returning
  `Results.Problem(statusCode: 400, …)` (`WorkEndpoints.cs:24,30,36,41`). Hand-rolled — there is
  no FluentValidation and no `[ApiController]` model binding here.
- **Invariants**, in the domain constructor/method, throwing `ArgumentException` or
  `InvalidOperationException`, caught at the endpoint and turned into a 400
  (`WorkEndpoints.cs:26,32`).

Everything that escapes lands in `ApiExceptionHandler.cs:11-21`, which owns the taxonomy:
`TenantAccessException` → 403, `DbUpdateConcurrencyException` → 409, `BadHttpRequestException`
→ 400, anything else → 500 with a logged `traceId`. Add a case there rather than catching
broadly at a call site.

Optimistic concurrency is PostgreSQL `xmin` as an EF shadow property
(`OperationsStore.cs:40`, `entity.Property<uint>("Version").IsRowVersion()`). The client sends
the version back, a mismatch surfaces as `DbUpdateConcurrencyException`, and the endpoint or the
handler returns 409.

## The capability model

- `Capabilities.All` (`Capabilities.cs:13`) drives one authorization policy per capability,
  generated in a loop at `src/PropFlow.Api/Program.cs:74-79`, each requiring an authenticated user plus a
  `capability` claim (`TenantAccess.cs:13`).
- `Capabilities.ForRole` (`Capabilities.cs:17-26`) is the **single** role→capability switch.
- **Endpoints never compare role names.** They call `.RequireAuthorization(Capabilities.X)`. A
  new permission means a new constant in `Capabilities.cs`, an entry in `All`, and a line in
  `ForRole` — not an `if (role == …)`.

`Capabilities.cs` is a shared merge point; append rather than rewrite.
`docs/architecture.md:29` describes an older, narrower role map — the code is the authority.
