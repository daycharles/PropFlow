# Local development

## First setup (Windows PowerShell)

Prerequisites: .NET 10 SDK 10.0.3xx, Docker running Linux containers, and a free local port 5432. PostgreSQL binds only to loopback. No public registration or hard-coded demo credentials exist.

```powershell
./scripts/Initialize-Local.ps1 -AdminEmail 'you@example.com' -OrganizationName 'Tidewater Residential Management' -AdminPassword (Read-Host 'New administrator password' -AsSecureString)
dotnet dev-certs https --trust
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/PropFlow.Api --urls https://localhost:7080
```

Run locally as `Development`. In `Production` or `Staging` the API refuses to start without
`DataProtection:KeyPath` — a persistent directory, shared by every instance, holding the
key ring that protects the auth and antiforgery cookies (optionally encrypted with
`DataProtection:CertificatePath` / `DataProtection:CertificatePassword`).

In a second PowerShell window, start the web client:

```powershell
cd apps/web
Copy-Item .env.example .env.local
npm ci
npm run dev
```

Then set `PROPFLOW_API_ORIGIN=https://localhost:7080` in `apps/web/.env.local` and open
`http://localhost:3000`. `.env.example` ships `https://localhost:5001` and `next.config.ts`
falls back to the same value when the variable is unset, so the copied file must be edited to
match the port the API is actually listening on — otherwise every `/api` call proxies nowhere. The browser talks to Next.js on the same origin; Next.js
proxies `/api/*` to the API so secure session and antiforgery cookies remain browser-visible.

Use a password of at least 12 characters with uppercase, lowercase, a digit, and punctuation. The helper creates ignored `.env` credentials only if the file is absent, starts PostgreSQL, applies migrations, configures a restricted database role, and creates the first organization/admin. It prints the organization ID and slug (the slug is used for login) and sets the runtime connection in the current shell. Bootstrap refuses existing accounts and never resets a password. Keep `.env` private. The development certificate trust command is an explicit local developer step; the application does not change certificate trust itself.

On subsequent runs set `ConnectionStrings__Database` from your private local configuration and start the API. Do not point the API at the PostgreSQL owner/admin account: startup rejects superuser, RLS-bypass, administrative, and schema/table-owner roles.

## Manual administration / other platforms

Set environment variables in your shell or secret manager, then run the commands below. Do not commit connection strings or passwords. `dotnet` does not automatically read `.env`; Compose and the PowerShell helper do.

| Command | Required environment variables |
| --- | --- |
| `dotnet run --project tools/PropFlow.Admin -- migrate` | `ConnectionStrings__Admin` |
| `dotnet run --project tools/PropFlow.Admin -- configure-runtime` | `ConnectionStrings__Admin`, `Runtime__Password` (20+ characters) |
| `dotnet run --project tools/PropFlow.Admin -- bootstrap` | `ConnectionStrings__Admin`, `Bootstrap__Organization`, `Bootstrap__Email`, `Bootstrap__Password` |
| `dotnet run --project tools/PropFlow.Admin -- seed-demo` | `ConnectionStrings__Admin`, `Demo__Password` (12+ characters) |
| `dotnet run --project src/PropFlow.Api --urls https://localhost:7080` | `ConnectionStrings__Database` (username `propflow_app`) |

Migration order is Identity, then Operations, then Communications, then Integrations — four contexts with four separate histories (`src/PropFlow.Infrastructure/Persistence/DatabaseProvisioner.cs`). The admin command uses a privileged migration connection and never runs inside the API. `configure-runtime` creates or rotates the `propflow_app` password and grants only the current milestone's required privileges. It is intended for a dedicated PropFlow database/role, not an unrelated existing database. Runtime cannot create users/memberships or alter schema. Future provisioning and invitation APIs must use a separately reviewed boundary.

## Demo data

After applying the M3 migrations, `seed-demo` creates (or preserves) two organizations:
Tidewater Residential Management and an isolation tenant. It adds a portfolio/property/building/space,
vendor, employee, two categories, and 12 work items per organization covering **all eight**
`WorkStatus` values (Draft, New, Assigned, Scheduled, InProgress, OnHold, Completed, Cancelled)
and **all four** `WorkPriority` values (Low, Normal, High, Critical), with a spread of overdue
and upcoming due dates. Four of the twelve stay in `New`, so a "filter to New, select all,
assign vendor" demo always has work to act on. The `Draft` row is the one that is never
published — `WorkItem.ChangeStatus` refuses a move back to `Draft`. The command is safe to
rerun: it never resets accounts and leaves an organization with existing work untouched. Sign in as `demo-admin@tidewater.example.test` using `Demo__Password`.
The second tenant uses `demo-admin@isolation.example.test` with the same password; use it only to
exercise tenant-isolation checks. These credentials are local demo data, not production defaults.

Local connection format: `Host=localhost;Database=propflow;Username=propflow_app;Password=<private runtime password>`. Use TLS with certificate validation for non-local PostgreSQL and HTTPS for API traffic. For multiple API instances, configure a shared encrypted ASP.NET Data Protection key store; keys and cookies must not be baked into images. The current default is single-host local key storage.

## Login API

1. GET `/api/auth/csrf`; retain the secure antiforgery cookie and returned `token`.
2. POST `/api/auth/login` with that token in `X-CSRF-TOKEN` and JSON containing `organizationSlug`, `email`, and `password`.
3. After a 204 response, GET `/api/auth/csrf` again to get a token bound to the signed-in identity.
4. GET `/api/session` to inspect the verified organization and capabilities.
5. Include the new CSRF token with every POST, including logout and vendor assignment.

Clients must preserve cookies and use HTTPS. The Next.js client is available in `apps/web`;
see `api.md` for endpoints and semantics.

## Verification

```powershell
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx --configuration Release --no-restore
dotnet run --project tests/PropFlow.FoundationChecks --configuration Release --no-build
dotnet test tests/PropFlow.UnitTests --configuration Release --no-build
dotnet test tests/PropFlow.IntegrationTests --configuration Release --no-build
Push-Location apps/web
npm ci
npm run format
npm run lint
npm run build
Pop-Location
```

Integration tests provision a disposable PostgreSQL 17 container with random credentials, apply real migrations, configure the restricted runtime account, and exercise the actual API with ASP.NET's test host over HTTPS semantics. Docker must be accessible. Each test creates separate organizations; no existing development data is changed. Testcontainers removes the test database/container afterward.

To add migrations, run `dotnet tool restore`, then `dotnet ef migrations add NAME --project src/PropFlow.Infrastructure --context IdentityStore` (or `OperationsStore`). Keep each context's migrations in its existing subfolder. Every new business table needs the central EF tenant convention, a composite tenant foreign key where appropriate, and a reviewed PostgreSQL RLS policy. Schema changes are not auto-applied on API startup.

## Slack build notifications

`scripts/Send-SlackUpdate.ps1` posts build/test/PR status to the Slack `#agent-updates` channel
through a Slack incoming webhook. The CI `verify` job calls it as its last step
(`.github/workflows/ci.yml`), on `push` events only — `on: [push, pull_request]` fires twice for
a PR branch, and two identical messages per commit is noise.

The webhook URL is a credential and is never committed. Two places supply it:

- **CI** — repository secret `PROPFLOW_SLACK_WEBHOOK_URL`:

  ```powershell
  gh secret set PROPFLOW_SLACK_WEBHOOK_URL --repo daycharles/PropFlow
  ```

- **Locally** — the gitignored `.claude/settings.local.json`:

  ```json
  { "env": { "PROPFLOW_SLACK_WEBHOOK_URL": "https://hooks.slack.com/services/..." } }
  ```

Create the webhook at <https://api.slack.com/apps> → your app → **Incoming Webhooks** →
**Activate Incoming Webhooks** → **Add New Webhook to Workspace** → pick `#agent-updates`. It is
post-only and Slack binds it to that one channel, so it cannot read the channel or post anywhere
else.

Run it by hand with:

```powershell
./scripts/Send-SlackUpdate.ps1 -Status info -Text 'PropFlow agent connected'
./scripts/Send-SlackUpdate.ps1 -Status fail -Text 'IntegrationTests: 3 failed' -Link $runUrl
```

The script exits non-zero when the URL is unset or is not a `hooks.slack.com` URL, rather than
silently doing nothing. The CI step is `continue-on-error: true` so that a missing secret or a
Slack outage cannot redden an otherwise green build; the failure is still visible in the step log.
