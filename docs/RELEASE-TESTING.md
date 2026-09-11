# Release testing — standing up `v1.0.0-rc.3` on macOS

A from-scratch walkthrough for running PropFlow locally and exercising it. Written for
**macOS / zsh**; every command is bash-compatible and ported from the CI `e2e` job
(`.github/workflows/ci.yml:62-128`), which runs this exact sequence on Linux on every push.

> **What is and is not verified.** The command sequence below is the one CI runs green on
> `ubuntu-latest`. It was also dry-run end to end on a Windows box before this release was
> tagged — `down -v` onward: fresh volume, all four migrations, `configure-runtime`, `seed-demo`,
> the API healthy on `https://localhost:5001`, `next dev` on `127.0.0.1:3000`, sign-in as
> `tidewater-demo`, 100 seeded work items, and a 10-item bulk vendor assignment that moved every
> one to `Assigned` with a `VendorAssigned` timeline entry.
>
> The **macOS-specific** steps — the keychain prompt on `dotnet dev-certs https --trust`, and
> Docker Desktop on Apple Silicon — were **not** executed by the author on macOS. If one of them
> behaves differently, that is the likeliest place for this guide to be wrong.

Every other doc in this repo uses PowerShell. This one does not, and `pwsh` is **not** required:
the bash path below replaces `scripts/Initialize-Local.ps1` entirely.

> **If you only want to run the app, use [DEPLOYMENT.md](DEPLOYMENT.md) instead.** Download a
> release bundle and run `./propflow-deploy up` — one executable that does everything below, in
> a Production posture, without needing the .NET SDK or a checkout. This document is the
> **Development** path: running from source with `dotnet run` and `next dev`, which is what you
> want for changing code, not for standing an instance up.

---

## 1. Prerequisites

| Thing | Version | Check |
| --- | --- | --- |
| .NET SDK | 10.0.3xx | `dotnet --version` — `global.json` pins `10.0.300` with `rollForward: latestPatch`, so any `10.0.3xx` patch is accepted |
| Node.js | 22 | `node --version` |
| Docker Desktop | any current | `docker version` — must be running |
| Port 5432 | free | `lsof -nP -iTCP:5432 -sTCP:LISTEN` must print nothing |

**Port 5432 is not negotiable.** `compose.yaml:8-9` binds `127.0.0.1:5432:5432`. A Homebrew
PostgreSQL (`brew services list` → `postgresql@…  started`) will collide; stop it with
`brew services stop postgresql@16` (substitute your version) before bringing Compose up.

Get the source at the tag:

```bash
git clone https://github.com/daycharles/PropFlow.git
cd PropFlow
git checkout v1.0.0-rc.3
```

---

## 2. Trust and export the development certificate

The API serves HTTPS and the web app proxies to it. Two separate things need the certificate.

```bash
dotnet dev-certs https --trust
```

On macOS this opens a **keychain prompt** — approve it. That covers the browser and `curl`.

`next dev` needs more. Its `/api` proxy goes through **undici**, which ignores
`NODE_TLS_REJECT_UNAUTHORIZED` and fails with `DEPTH_ZERO_SELF_SIGNED_CERT`. Pointing Node's CA
store at an exported PEM is what actually works (`ci.yml:84-90`):

```bash
dotnet dev-certs https --export-path ~/propflow-devcert.pem --format PEM --no-password
test -s ~/propflow-devcert.pem || echo "FAILED: no PEM was written — the API will be unreachable from next dev"
```

---

## 3. Environment

Two places need configuration: a `.env` at the repo root, which `docker compose` interpolates,
and the shell that runs the .NET commands.

`compose.yaml:7` declares `POSTGRES_PASSWORD` as **required** — without it even
`docker compose down -v` fails (`ci.yml:150-156`). Write the file first:

```bash
cat > .env <<'EOF'
POSTGRES_PASSWORD=propflow-local
EOF
```

Then, in the shell you will use for every command below:

```bash
export POSTGRES_PASSWORD='propflow-local'

# Privileged connection. Used by PropFlow.Admin only — never by the API.
export ConnectionStrings__Admin='Host=127.0.0.1;Port=5432;Database=propflow;Username=propflow;Password=propflow-local'

# The restricted runtime role's password. MUST be 20+ characters, or configure-runtime refuses
# the role (src/PropFlow.Infrastructure/Persistence/DatabaseProvisioner.cs:45), and MUST be
# byte-identical to the password inside ConnectionStrings__Database below.
export Runtime__Password='runtime-local-not-a-secret-01'
export ConnectionStrings__Database='Host=127.0.0.1;Port=5432;Database=propflow;Username=propflow_app;Password=runtime-local-not-a-secret-01'

# Demo account password. 12+ characters (tools/PropFlow.Admin/Program.cs:93). This is what you
# sign in with.
export Demo__Password='DemoPassword!123'

# The API listens here. `localhost`, not 127.0.0.1 — see section 5.
export ASPNETCORE_URLS='https://localhost:5001'
export ASPNETCORE_ENVIRONMENT='Development'

# Where next dev proxies /api. Same value; next.config.ts:7 defaults to it anyway.
export PROPFLOW_API_ORIGIN='https://localhost:5001'

# The PEM from section 2.
export NODE_EXTRA_CA_CERTS="$HOME/propflow-devcert.pem"
```

`Development` is the intended local posture. In `Production` or `Staging` the API refuses to
start without `DataProtection:KeyPath` (`src/PropFlow.Api/Program.cs:48-63`) and adds HSTS.

Two traps worth restating, both of which produce confusing failures rather than clear ones:

- **`Runtime__Password` under 20 characters** → `configure-runtime` throws
  `Runtime password must contain at least 20 characters.`
- **`Runtime__Password` not matching the password inside `ConnectionStrings__Database`** →
  `configure-runtime` succeeds, then the API fails to authenticate as `propflow_app` at startup.

---

## 4. Bring the stack up

Order matters. Each step depends on the one before it.

```bash
# 1. Build once, in Release, so every --no-build below is honest.
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx --configuration Release --no-restore
(cd apps/web && npm ci)

# 2. PostgreSQL 17. --wait blocks until the healthcheck passes.
docker compose up -d --wait

# 3. Schema, restricted role, demo data. The API never does any of this.
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- migrate
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- configure-runtime
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- seed-demo

# 4. The API, backgrounded, with its log on disk.
dotnet run --project src/PropFlow.Api --configuration Release --no-build > /tmp/propflow-api.log 2>&1 &

# 5. The web app, backgrounded. --hostname 127.0.0.1 matters; see section 5.
(cd apps/web && npm run dev -- --hostname 127.0.0.1 > /tmp/propflow-web.log 2>&1) &
```

`migrate` applies **four** contexts with four separate histories — identity, operations,
communications, integrations (`DatabaseProvisioner.MigrateAsync`).

Wait for both before opening a browser. The API answers `/health/ready` with **503** until the
schema and RLS readiness checks pass, so poll that rather than the socket:

```bash
for i in $(seq 1 60); do
  curl -fsS --cacert "$NODE_EXTRA_CA_CERTS" https://localhost:5001/health/ready >/dev/null 2>&1 && { echo "API ready"; break; }
  sleep 1
done
for i in $(seq 1 60); do
  curl -fsS http://127.0.0.1:3000 >/dev/null 2>&1 && { echo "Web ready"; break; }
  sleep 1
done
```

If either loop runs out, **read the log** — `/tmp/propflow-api.log`, `/tmp/propflow-web.log`. An
absent or zero-byte log is not a clean log; it means the process never started.

Tear down when finished:

```bash
kill %1 %2 2>/dev/null   # the two background jobs
docker compose down -v   # -v also drops the data volume
```

---

## 5. The two host names, which are not interchangeable

This is the single most common way to get a stack that starts and then cannot log in.

- **The API must listen on `https://localhost:5001`.** The ASP.NET development certificate's SAN
  is `localhost`, so `https://127.0.0.1:5001` fails hostname verification and the Next.js proxy
  cannot reach it.
- **The API must be HTTPS.** The session and antiforgery cookies are `__Host-` prefixed with
  `SecurePolicy = Always` (`src/PropFlow.Api/Program.cs:106-108,120-122`). Over plain http the
  antiforgery system throws, `GET /api/auth/csrf` returns 500, and login is impossible.
- **The browser must open `http://127.0.0.1:3000`.** That is a potentially-trustworthy origin, so
  Chrome still accepts the `Secure` / `__Host-` cookies the API sets through the proxy. The
  browser never talks to the API directly — everything goes through the Next.js `/api` rewrite
  (`apps/web/next.config.ts:3-10`), which is what keeps the cookies same-origin.

Also worth knowing: **the API is configured by environment variables only.** There is no
`appsettings.json` in `src/PropFlow.Api/`. If a setting appears not to apply, it is almost always
that the variable was exported in a different shell from the one running the API.

---

## 6. Sign in

Open <http://127.0.0.1:3000>. The login form takes **organization slug + email + password**
(`apps/web/app/page.tsx:82-95`). `seed-demo` creates three accounts, all on the value of
`Demo__Password`:

| Slug | Email | Role | Use it for |
| --- | --- | --- | --- |
| `tidewater-demo` | `demo-admin@tidewater.example.test` | Organization Admin | Everything in section 7. Start here. |
| `tidewater-demo` | `demo-technician@tidewater.example.test` | Technician (bound to a seeded employee) | The field-role scope model and the technician mobile view |
| `isolation-demo` | `demo-admin@isolation.example.test` | Organization Admin | Tenant isolation only — a separate organization with a 12-item seed |

With the values in section 3 that is `tidewater-demo` /
`demo-admin@tidewater.example.test` / `DemoPassword!123`.

> **`DemoPassword!123` belongs to this document only.** It is the value *you* exported as
> `Demo__Password` in section 3, so it is the password only because you chose it here. An install
> made with `propflow-deploy` from a release bundle seeds a different one — see
> [DEPLOYMENT.md](DEPLOYMENT.md), or run `propflow-deploy credentials`. Carrying this password
> over to a bundle install is the single most common way to be locked out.

The technician account is deliberately narrower: a field role is inert until its membership names
an employee, and is then scoped to work assigned to that employee. Seeing fewer work items there
is the feature, not a bug.

---

## 7. What to exercise

[docs/demo-script.md](demo-script.md) is the ordered walkthrough and runs about six minutes. It
**leads with bulk vendor assignment** — that is the headline, the thing a spreadsheet cannot do,
and the feature our property-management consultant singled out. Follow it first.

The short version, on the Tidewater org (100 work items, 16 of them `New`):

1. **Bulk vendor assignment** — filter **Status → New**, tick the header checkbox, **Assign
   vendor only** → pick a vendor → **Confirm**. Then open one of those items and confirm the
   timeline carries a `VendorAssigned` entry. This is the demo.
2. **Assign & notify** — the same selection through the M4 flow: vendor + an optional visit
   window + a resident message template, with a per-step outcome summary and
   `MessageQueued` / `MessageSent` on the timeline.
3. **Filters and saved views** — filter and sort the work list, save the view, reload, reapply.
4. **The append-only timeline** — every change on a work item, in order. It cannot be edited or
   deleted, at three enforcement levels.
5. **Tenant isolation** — sign out, sign in as `isolation-demo`, and confirm none of Tidewater's
   work, vendors or residents are visible.

Then the M5 and M6 surfaces:

6. **Technician "on the way"** — sign in as the technician account, open an assigned item, mark
   on the way, and watch the status and timeline entry appear.
7. **Automation rules** — `/settings/automation`. Create a WHEN/IF/THEN rule (for example, a new
   Critical work item gets its priority set, or a resident-visible note notifies the resident),
   then trigger it and confirm it fired once.
8. **Asset detail** — `/assets/[id]` from a work item's asset link: the record, its maintenance
   history newest-first, and the work-order-count / total-cost / age roll-ups. Where an asset has
   three repairs in the window, the repeat-repair warning shows on both the asset and work pages.
9. **Attention queue** — `/attention`. Critical / Warning / Informational cards, each filtering
   the list to the rows that count; every row links to its work order.
10. **Global search** — `Cmd+K` (or `/`, or the header button), arrow keys to navigate. Note the
    limitation in section 10: building / resident / vendor / employee hits dead-end.
11. **Integration health** — `/integrations`: per-connection status, last sync, failure count,
    tracked records, Sync-now, enable/disable, and the expandable per-record list. The adapter is
    a mock.
12. **Attachments** — the attachments panel on a work item (upload a PDF or a photo, ≤ 25 MB) and
    the "Add photo" flow on the technician view.

---

## 8. Re-seeding between runs

**The demo consumes its own data.** Bulk assignment moves every `New` item to `Assigned`, so a
second run of the script finds nothing to assign (`docs/demo-script.md:43-45`).

Both seeders are **idempotent by skip**, not by reset: they return early if the organization
already has work items (`tools/PropFlow.Admin/TidewaterSeed.cs:80`,
`tools/PropFlow.Admin/Program.cs:165`). Re-running `seed-demo` therefore changes nothing — it
does not restore the `New` items. To get a clean dataset you must drop the volume:

```bash
docker compose down -v
docker compose up -d --wait
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- migrate
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- configure-runtime
dotnet run --project tools/PropFlow.Admin --configuration Release --no-build -- seed-demo
```

The seed is deterministic — a fixed RNG seed — so every reset produces the identical dataset.

---

## 9. Health endpoints

| Endpoint | What it tells you |
| --- | --- |
| `GET /health/live` | The process is up. Says nothing about the database. |
| `GET /health/ready` | **503 until the schema and RLS checks pass.** This is the one to gate on. |
| `GET /health/metrics` | The observability snapshot (PF-7.03). |

Defined at `src/PropFlow.Api/Program.cs:185-187`. Over HTTPS with the dev cert, `curl` needs
`--cacert "$NODE_EXTRA_CA_CERTS"`:

```bash
curl -fsS --cacert "$NODE_EXTRA_CA_CERTS" https://localhost:5001/health/ready
curl -fsS --cacert "$NODE_EXTRA_CA_CERTS" https://localhost:5001/health/metrics
```

A `ready` that stays 503 means the schema is missing or RLS is not `ENABLE`d **and** `FORCE`d —
in practice, a `migrate` or `configure-runtime` that did not run or did not succeed.

---

## 10. Known limitations in this release candidate

Carried from [CHANGELOG.md](../CHANGELOG.md) and [docs/followups.md](followups.md). None of these
is a defect to report — they are the accepted state of `v1.0.0-rc.3`.

- **Resident messaging is mocked.** Messages are recorded in the outbox and shown on the
  timeline; nothing is delivered to a real phone or inbox. Real providers are configurable
  (PF-7.05) but unconfigured here.
- **No proven upgrade path.** The `M3Operations` migration set is not squashed. A fresh install
  is supported; migrating a populated pre-M3 database is not.
- **The `identity` schema has no RLS** (audit-security M-1). Not exploitable today — there is no
  member-listing endpoint — but it is an open design decision.
- **The attention queue and global search are unpaged**, and global-search hits for
  building / resident / vendor / employee dead-end at a title-only work search.
- **No settings screen for the repeat-repair policy** — threshold, window and category similarity
  are API-only. **Integration sync** tracks external records without reconciling them into the
  domain tables, so the "unresolved conflicts" count stays 0.
- **No container image and no hosted environment.** Source build only.
- **Web CSP and the http→https redirect are left to an ingress or proxy.** The API itself already
  sends HSTS (outside Development) and the rest of the security header set.

---

## 11. If something goes wrong

| Symptom | Almost always |
| --- | --- |
| `docker compose up` fails on a port | A Homebrew PostgreSQL on 5432. `brew services stop postgresql@…` |
| `docker compose down -v` fails | `POSTGRES_PASSWORD` unset in this shell and no `.env` at the repo root |
| `configure-runtime` throws about password length | `Runtime__Password` under 20 characters |
| API exits at startup, log mentions the runtime role | `Runtime__Password` and the password inside `ConnectionStrings__Database` differ |
| API exits at startup, log mentions a privileged connection | `ConnectionStrings__Database` points at the owner account. The API refuses superuser / RLS-bypass / owner roles by design |
| Login returns 500 from `/api/auth/csrf` | The API is on http, or on `127.0.0.1` instead of `localhost` |
| `next dev` logs `DEPTH_ZERO_SELF_SIGNED_CERT` | `NODE_EXTRA_CA_CERTS` is unset, or points at a file that was never written |
| `⨯ Another next dev server is already running` | An earlier `next dev` for this directory survived. It names the PID and an alternate port in the message — stop that PID rather than using the other port, or the browser and the proxy disagree about which server is live |
| `/health/ready` never leaves 503 | `migrate` or `configure-runtime` did not actually succeed — re-read their output |
| Nothing to bulk-assign | The demo already consumed the `New` items. Section 8 — `down -v` and re-seed |

Report anything else against the repository, with the relevant slice of
`/tmp/propflow-api.log` or `/tmp/propflow-web.log`.
