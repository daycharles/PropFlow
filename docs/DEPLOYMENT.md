# PropFlow — install and run

This is the complete guide for installing PropFlow on one machine and signing in. It is also the
`README.md` inside every release bundle.

For running from source while changing code (`dotnet run`, `next dev`), see
[RELEASE-TESTING.md](https://github.com/daycharles/PropFlow/blob/main/docs/RELEASE-TESTING.md) instead — **the credentials in that document are for that
path only and will not work with a bundle installed from here.**

---

## Sign-in credentials

The form at <http://127.0.0.1:3000> asks for **three** fields. The organization slug is the one
people miss — it is not optional, and an email and password alone will not get you in.

| Field | Value |
| --- | --- |
| **Organization slug** | `tidewater-demo` |
| **Email** | `demo-admin@tidewater.example.test` |
| **Password** | `PropFlowDemo!2026` |

Two more accounts are seeded, with the same password:

| Slug | Email | What it is |
| --- | --- | --- |
| `tidewater-demo` | `demo-technician@tidewater.example.test` | A **Technician**. Deliberately sees only work assigned to them — a shorter list here is the access model working, not a fault. |
| `isolation-demo` | `demo-admin@isolation.example.test` | A **separate organization** with its own small dataset, for checking tenant isolation. |

These are also written to `SIGN-IN.txt` in the state directory (section 7) every time the stack
starts, and `./propflow-deploy credentials` reprints them at any time.

To choose your own password, set `PROPFLOW_DEMO_PASSWORD` **before the first `up`** — it must be
12+ characters with upper case, lower case, a digit and punctuation. After the first run the
accounts exist and changing the variable does nothing.

---

## 1. What you need first

| Requirement | Why | Check |
| --- | --- | --- |
| **Docker Desktop**, running | PostgreSQL 17 runs in a container | `docker info` prints a server version |
| **Node.js 22** | The web app is a Next.js server | `node --version` |
| Ports **5432**, **5001**, **3000** free | Database, API, web app | see below |

You do **not** need the .NET SDK, a checkout, or `git`. The API, admin tool and deployer are
self-contained builds carrying their own runtime.

A Homebrew PostgreSQL is the usual cause of a busy 5432:

```bash
brew services list              # look for postgresql@NN  started
brew services stop postgresql@16
```

On Windows, check a port with `netstat -ano | findstr :5432`.

---

## 2. Install

### macOS

```bash
tar xzf propflow-1.0.0-rc.3-osx-arm64.tar.gz      # Apple Silicon (M1/M2/M3/M4)
# or
tar xzf propflow-1.0.0-rc.3-osx-x64.tar.gz        # Intel Mac

cd propflow-1.0.0-rc.3-osx-arm64
```

**Unpack with `tar`, not by double-clicking in Finder.** `tar` preserves the executable bit;
Archive Utility does not, and you will get `permission denied`.

macOS may also quarantine a downloaded binary. If you see *"cannot be opened because the developer
cannot be verified"*, clear it once:

```bash
xattr -dr com.apple.quarantine .
```

### Windows

Either run **`propflow-1.0.0-rc.3-win-x64.msi`** — which installs to `C:\Program Files\PropFlow`,
adds Start Menu shortcuts, and puts `propflow-deploy` on your `PATH` — or unzip
`propflow-1.0.0-rc.3-win-x64.zip` anywhere and work inside it.

---

## 3. Start it

```bash
./propflow-deploy up
```

On Windows with the MSI, use the **Start PropFlow** shortcut, or `propflow-deploy up` from any
terminal.

It prints what it is doing, in six steps:

1. **Starts PostgreSQL** and waits for its health check.
2. **Applies migrations** — four schemas: identity, operations, communications, integrations.
3. **Configures the restricted database role** the API is allowed to use.
4. **Seeds demo data** — two organizations, 100 work orders. Skip with `--no-seed`.
5. **Builds the web app** — `npm ci` then `next build`. **This is the slow step and only happens
   once**; expect a few minutes on a first run.
6. **Starts the API and web app** and waits until both answer.

Before all that it runs a preflight and, if anything is wrong, reports **every** problem at once
with a fix for each rather than failing one at a time.

When it finishes it prints the URL and the credentials. Then open **<http://127.0.0.1:3000>**.

`up` is safe to re-run: it reuses the credentials, certificate and web build it made the first
time, so a second start takes seconds.

### The other commands

```bash
./propflow-deploy status          # ports, API health, where state lives
./propflow-deploy credentials     # reprint the sign-in accounts
./propflow-deploy down            # stop everything, keep the data
./propflow-deploy down --reset    # stop and drop the database
./propflow-deploy up --no-seed    # start with no demo data
./propflow-deploy --help
```

---

## 4. What to look at

[demo-script.md](https://github.com/daycharles/PropFlow/blob/main/docs/demo-script.md) is the ordered walkthrough, about six minutes. It **leads with
bulk vendor assignment** — the headline feature.

The short version, signed in as `tidewater-demo` / `demo-admin@tidewater.example.test`:

1. **Bulk vendor assignment.** Filter **Status → New**, tick the header checkbox, **Assign vendor
   only** → pick a vendor → **Confirm**. Open one of those work orders and see `VendorAssigned` on
   its timeline. This is the thing to see first.
2. **Assign & notify** — the same selection with a visit window and a resident message, with a
   per-step summary.
3. **Filters and saved views** — filter, sort, save the view, reload, reapply.
4. **The timeline** — append-only, enforced at three levels; it cannot be edited or deleted.
5. **Tenant isolation** — sign out, sign in as `isolation-demo`, confirm none of Tidewater's data
   is visible.
6. **Technician "on the way"** — sign in as the technician account, open assigned work, mark on
   the way.
7. **Automation rules** — `/settings/automation`. Create a WHEN/IF/THEN rule, trigger it.
8. **Asset detail and repeat-repair** — an asset's maintenance history and roll-ups, with the
   repeat-repair warning where it applies.
9. **Attention queue** — `/attention`, with Critical / Warning / Informational cards.
10. **Global search** — `Cmd+K` (macOS) or `Ctrl+K`.
11. **Integration health** — `/integrations`. The adapter is a mock.
12. **Attachments** — upload a PDF or photo on a work order, ≤ 25 MB.

### Re-running the demo

The demo **consumes its own data**: bulk assignment moves every `New` item to `Assigned`, so a
second run finds nothing to assign. The seeders are idempotent *by skip*, so re-running `up` does
not restore it. For a clean dataset:

```bash
./propflow-deploy down --reset
./propflow-deploy up
```

The seed is deterministic, so every reset produces exactly the same data.

---

## 5. If something goes wrong

| Symptom | Cause and fix |
| --- | --- |
| **Login is rejected** | The organization slug is required — `tidewater-demo`, not blank. Check the password with `./propflow-deploy credentials`; do not use the one in `RELEASE-TESTING.md`, which is for the from-source path. |
| **Login says too many attempts** | Production rate-limits sign-in to 10/minute. Wait a minute. |
| **The page will not load** | The stack is not running. `./propflow-deploy status`, then `up`. |
| **Technician sees very little work** | Working as designed: a Technician is scoped to work assigned to them. Use the admin account for the full list. |
| `cannot be opened because the developer cannot be verified` | Gatekeeper quarantine. `xattr -dr com.apple.quarantine .` |
| `permission denied: ./propflow-deploy` | Unpacked in Finder instead of with `tar`. `chmod +x propflow-deploy api/PropFlow.Api admin/PropFlow.Admin` |
| Preflight: Docker not responding | Docker Desktop is not started, or still starting. |
| Preflight: port 5432 in use | A local PostgreSQL. `brew services stop postgresql@NN` |
| Preflight: port 3000 or 5001 in use | A previous deployment. `./propflow-deploy down` |
| `/health/ready` never leaves 503 | Migrations or the runtime role did not apply. Read `logs/api.log` in the state directory. |
| The web app never becomes ready | Read `logs/web.log`. A failed `npm ci` — usually no network to the npm registry — is the common cause. |
| Nothing left to bulk-assign | The demo consumed the `New` items. `down --reset` then `up`. |

An **absent** log file is not a clean log — it means the process never started. The deployer says
which of the two happened rather than printing nothing.

---

## 6. What "production posture" means here

The API runs with `ASPNETCORE_ENVIRONMENT=Production`, which this codebase treats as a real mode
rather than a label. It *refuses to start* without three things, all supplied by the deployer:

| Enforced | Where |
| --- | --- |
| A persistent Data Protection key ring | `src/PropFlow.Api/Program.cs:58-62` |
| An explicit forwarded-headers trust boundary | `src/PropFlow.Api/DeploymentConfiguration.cs:40-43` |
| A configured attachment root | `src/PropFlow.Infrastructure/Attachments/LocalAttachmentStorage.cs:52-54` |

Also real: the restricted database role (the API refuses an owner/superuser connection), row-level
security `ENABLE` **and** `FORCE` on every business table, the production login rate limit, and
the full security header set. The web app is `next build` + `next start`, not a dev server.

**You will not see a `Strict-Transport-Security` header, and that is correct.** `UseHsts()` is
registered outside Development but is called without options, so ASP.NET's default excluded-hosts
list suppresses it on `localhost` / `127.0.0.1` / `[::1]`. A deployment on a real hostname gets
it. The other five headers are present.

### Two deliberate deviations

Both are what a real deployment would replace:

1. **TLS is terminated by Kestrel** with a self-signed certificate the deployer issues
   (`CN=localhost`, one year), not by an ingress with a CA-issued one. Nothing is added to your OS
   trust store: the only process that must trust it is Node, which is given the PEM through
   `NODE_EXTRA_CA_CERTS`, and your browser never talks to the API directly — it reaches the web
   app over `http://127.0.0.1:3000`, a potentially-trustworthy origin, so the `Secure` / `__Host-`
   cookies the API sets through the proxy are still accepted.
2. **The forwarded-headers allow-list is loopback**, because there is no proxy in front. The
   setting is required in Production and so is supplied, but it is not doing real work here.

**Messaging is the mock outbox.** Resident messages are recorded and appear on the timeline;
nothing is delivered to a real phone or inbox.

---

## 7. Where everything lives

Everything the deployment creates sits in one state directory. Deleting it is a factory reset.

| Where it is | When |
| --- | --- |
| `state/` next to the executable | You unpacked an archive |
| `%ProgramData%\PropFlow` | Windows, installed by the MSI |
| `~/Library/Application Support/PropFlow` | macOS, if the app directory is not writable |

`./propflow-deploy status` prints the actual path. Override it with `--state <path>` or
`PROPFLOW_STATE`.

```
SIGN-IN.txt              the accounts above, written on every start
deployment.json          generated credentials (0600 on Unix)
certs/                   propflow.pfx (Kestrel) and propflow.pem (Node)
dataprotection-keys/     the key ring protecting auth and antiforgery cookies
attachments/             attachment blobs
logs/api.log             API output
logs/web.log             web app output
processes.json           pids, so `down` can stop what `up` started
web/                     the web build, when the app directory is read-only
```

The database passwords are generated per install — 32 characters each, and the runtime role is
rejected below 20. The demo account password is the documented one above, because a person has to
type it.

This is **not** a secret-management story. A real deployment takes these from a secret manager
(PF-7.02). `deployment.json` is a plain file; treat the directory as you would any credential
store.

---

## 8. Building the bundles yourself

From a checkout, with the .NET SDK:

```powershell
./scripts/Publish-Deployment.ps1                                          # macOS, both architectures
./scripts/Publish-Deployment.ps1 -RuntimeIdentifier win-x64 -Installer    # Windows zip + MSI
```

Output lands in `artifacts/` (gitignored). Three things the script handles that are easy to get
wrong by hand:

- **Lock files.** A self-contained restore appends a per-RID section (`"net10.0/osx-arm64": {}`)
  to every committed `packages.lock.json`. `RestorePackagesWithLockFile=false` is not an escape —
  NuGet rejects it with **NU1005** when a lock file exists. So the script refuses to start if any
  lock file is already dirty, and reverts them in a `finally` — on failure as well as success,
  because a half-finished publish is exactly what leaves them dirty for the next run.
- **The executable bit.** Windows filesystems have none, so an archive built there unpacks with
  `propflow-deploy` non-executable. The script uses **GNU tar**'s `--mode=a+rx`. Windows' bundled
  `tar` is bsdtar and rejects `--mode`; Git for Windows ships GNU tar, located by deriving it from
  wherever `git` itself is installed. GNU tar on Windows also needs `--force-local`, or `C:\...`
  is read as a remote host, and its own directory on `PATH` so `--gzip` can find `gzip`.
- **The MSI** needs the WiX CLI: `dotnet tool install --global wix`. Windows bundles are zipped
  rather than tarred — Explorer opens zip natively and there is no execute bit to preserve.

Verify a built archive with `tar -tvzf …`; the binaries should read `-rwxr-xr-x`.
