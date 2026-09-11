# Deploying PropFlow

How to stand PropFlow up on one machine from a release bundle, using the `propflow-deploy`
executable. This is the **production-posture** path. For running from source in Development —
`dotnet run`, `next dev`, hot reload — use [RELEASE-TESTING.md](RELEASE-TESTING.md) instead.

> **What is verified.** The `win-x64` bundle was built and run end to end against a real
> PostgreSQL 17: preflight correctly refused a busy port 5432; `up` completed all six steps;
> the API reported `Hosting environment: Production` and `/health/ready` 200; the Data Protection
> key ring was written to `state/dataprotection-keys/`; `https://127.0.0.1:5001` was rejected at
> TLS while `https://localhost:5001` succeeded, confirming the certificate's SAN; sign-in through
> the web proxy returned 204 with 14 capabilities; the seed produced **100 work items, 16 New**;
> a 10-item bulk vendor assignment returned `{"changed":10,"unchanged":0,"total":10}` and left a
> `VendorAssigned` timeline entry; and `down` followed by `up` came back in **11 seconds** with
> credentials, certificate, build and data all intact.
>
> The macOS bundles are cross-published from that same source and are **not** executed by the
> author on macOS. Apple Silicon, Gatekeeper quarantine and Docker Desktop for Mac behaviour are
> unverified — section 7 covers the two macOS-specific steps most likely to bite.

---

## 1. What you need

| Thing | Why |
| --- | --- |
| **Docker Desktop**, running | PostgreSQL 17 runs in a container. Nothing else does. |
| **Node.js 22** | The web app is a Next.js server; it needs a Node runtime. |
| Ports **5432**, **5001**, **3000** free | Database, API, web app. |

You do **not** need the .NET SDK. The API, the admin tool and the deployer are self-contained
builds that carry their own runtime.

A Homebrew PostgreSQL is the usual cause of a busy 5432:

```bash
brew services list            # look for postgresql@NN  started
brew services stop postgresql@16
```

---

## 2. Get the bundle

Download the archive for your machine from the
[release page](https://github.com/daycharles/PropFlow/releases) and unpack it:

```bash
# Apple Silicon (M1/M2/M3/M4)
tar xzf propflow-1.0.0-rc.1-osx-arm64.tar.gz
cd propflow-1.0.0-rc.1-osx-arm64

# Intel Mac
tar xzf propflow-1.0.0-rc.1-osx-x64.tar.gz
cd propflow-1.0.0-rc.1-osx-x64
```

Unpack with `tar`, not by double-clicking in Finder — `tar` preserves the executable bit.

---

## 3. Deploy

```bash
./propflow-deploy up
```

That is the whole thing. It runs six steps and prints what it is doing:

1. **Starts PostgreSQL** via `docker compose`, waiting for the health check.
2. **Applies migrations** — four contexts: identity, operations, communications, integrations.
3. **Configures the restricted runtime role** (`propflow_app`), which is the only account the
   API is allowed to connect as.
4. **Seeds demo data** — two organizations, ~100 work items. Skip with `--no-seed`.
5. **Builds the web app** — `npm ci` then `next build`. This is the slow step, first run only.
6. **Starts the API and the web app**, then waits for `/health/ready` and the web root.

On success it prints the URL and the sign-in credentials it generated.

Before any of that it runs a **preflight**: Docker present and responding, Node present and new
enough, npm present, the three ports free, and the bundle complete. Failures are reported as one
list with a fix for each, rather than one at a time.

`up` is **safe to re-run**. It reuses the credentials and certificate it generated the first
time, skips `npm ci` and `next build` if their output already exists, and reconnects to the same
database.

### Other commands

```bash
./propflow-deploy status          # ports and API health
./propflow-deploy down            # stop the stack, keep the data
./propflow-deploy down --reset    # stop the stack and drop the database volume
./propflow-deploy up --no-seed    # bring up an empty instance
./propflow-deploy --help
```

Re-seeding needs `down --reset` then `up`: the seeders are idempotent **by skip**, so simply
re-running `up` will not restore demo data that a walkthrough consumed.

---

## 4. Signing in

Open **<http://127.0.0.1:3000>**. The form wants **organization slug + email + password**. With
demo data seeded, `up` prints three accounts — all sharing one generated password:

| Slug | Email | Role |
| --- | --- | --- |
| `tidewater-demo` | `demo-admin@tidewater.example.test` | Organization Admin — start here |
| `tidewater-demo` | `demo-technician@tidewater.example.test` | Technician, bound to a seeded employee |
| `isolation-demo` | `demo-admin@isolation.example.test` | A second tenant, for isolation checks |

The password is also in `state/deployment.json` if you scroll past it.

Then follow [demo-script.md](demo-script.md), which leads with **bulk vendor assignment**.

---

## 5. What "production posture" means here

This is not `next dev` with a label changed. The API runs with
`ASPNETCORE_ENVIRONMENT=Production`, which the code treats as a real mode:

| Enforced | Where |
| --- | --- |
| A persistent Data Protection key ring, or startup throws | `src/PropFlow.Api/Program.cs:58-62` |
| An explicit forwarded-headers trust boundary, or startup throws | `src/PropFlow.Api/DeploymentConfiguration.cs:40-43` |
| A configured attachment root, or startup throws | `src/PropFlow.Infrastructure/Attachments/LocalAttachmentStorage.cs:52-54` |
| HSTS middleware registered (see the note below) | `Program.cs:157-158` |
| Login rate limit 10/min, not Development's 200 | `Program.cs:145` |
| The API refuses an owner/superuser database connection | `RuntimeDatabaseGuard` |
| Row-level security `ENABLE` **and** `FORCE` on every business table | readiness fails without both |

The web app runs `next build` + `next start` with `NODE_ENV=production` — a compiled server, not
a dev server.

**You will not see a `Strict-Transport-Security` header in this deployment, and that is correct.**
`Program.cs:157-158` registers `UseHsts()` outside Development, but it is called without options,
so ASP.NET's default `ExcludedHosts` applies — `localhost`, `127.0.0.1` and `[::1]` never receive
the header. A deployment on a real hostname gets it; a loopback one does not. Verified by reading
the response headers off the running stack: `X-Content-Type-Options`, `Referrer-Policy`,
`X-Frame-Options: DENY`, `Cross-Origin-Resource-Policy` and
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` are all present, and HSTS is
the only one absent.

### Two deliberate deviations

Both are honest gaps rather than oversights, and both are what a real deployment would replace:

1. **TLS is terminated by Kestrel**, using a self-signed certificate the deployer issues into
   `state/certs/` (`CN=localhost`, SAN `localhost`, one year). A real deployment terminates TLS
   at an ingress with a CA-issued certificate. Nothing is added to your OS trust store: the only
   process that must trust it is Node, which gets it through `NODE_EXTRA_CA_CERTS`, and your
   browser never talks to the API directly — it reaches the web app over
   `http://127.0.0.1:3000`, a potentially-trustworthy origin, so the `Secure` / `__Host-`
   cookies the API sets through the proxy are still accepted.
2. **The forwarded-headers allow-list is loopback**, because there is no proxy in front. The
   setting is required in Production and is therefore supplied, but it is not doing real work
   here.

Also unchanged from the release notes: **messaging is the mock outbox**. Messages are recorded
and appear on the timeline; nothing is delivered. Real providers are configurable (PF-7.05) and
are not configured by this bundle.

---

## 6. Where everything lives

Everything mutable is under `state/` in the bundle directory. Deleting it is a factory reset.

```
state/deployment.json          generated credentials (0600 on Unix)
state/certs/                   propflow.pfx (Kestrel) and propflow.pem (Node)
state/dataprotection-keys/     the key ring protecting auth and antiforgery cookies
state/attachments/             attachment blobs
state/logs/api.log             API stdout and stderr
state/logs/web.log             web app stdout and stderr
state/processes.json           pids, so `down` can stop what `up` started
```

The credentials are generated per install — a 32-character owner password, a 32-character
runtime password (the role is rejected below 20), a demo password, a certificate password and a
provider-callback signing secret. None of them are defaults, and none are in the repository.

This is **not** a secret-management story. A real deployment takes these from a secret manager;
PF-7.02 is where that belongs. `state/deployment.json` is a plain file — treat the directory the
way you would treat any credential store.

---

## 7. Troubleshooting

| Symptom | Cause |
| --- | --- |
| `"propflow-deploy" cannot be opened because the developer cannot be verified` | macOS Gatekeeper quarantined the download. `xattr -dr com.apple.quarantine .` in the bundle directory, or right-click → Open once. |
| `zsh: permission denied: ./propflow-deploy` | The executable bit was lost — you unpacked in Finder rather than with `tar`. `chmod +x propflow-deploy api/PropFlow.Api admin/PropFlow.Admin`. |
| Preflight: Docker not responding | Docker Desktop is installed but not started, or still starting. |
| Preflight: port 5432 in use | A local PostgreSQL. `brew services stop postgresql@NN`. |
| Preflight: port 3000 or 5001 in use | A previous deployment. `./propflow-deploy down`. |
| `/health/ready` never leaves 503 | Migrations or the runtime role did not actually apply. Read `state/logs/api.log`. |
| The web app never becomes ready | Read `state/logs/web.log`. A failed `npm ci` is the usual cause — check network access to the npm registry. |
| Nothing left to bulk-assign | The demo consumed the `New` items. `./propflow-deploy down --reset` then `up`. |

An absent log file is **not** a clean log — it means the process never started. The deployer says
which of those two happened rather than printing nothing.

---

## 8. Building the bundles yourself

From a checkout, with the .NET SDK installed:

```powershell
./scripts/Publish-Deployment.ps1                                  # osx-arm64 and osx-x64
./scripts/Publish-Deployment.ps1 -RuntimeIdentifier win-x64 -SkipArchive
```

Output lands in `artifacts/`. The script publishes self-contained, so a RID-specific restore
would normally rewrite every committed `packages.lock.json`; it passes
`RestorePackagesWithLockFile=false` to prevent that, and then **verifies** with `git status` that
no lock file changed, failing the publish if one did.
