# PropFlow demo script

The M3 walkthrough, in the order to show it. **Lead with bulk vendor assignment** — that is the
feature Emily, our property-management consultant, singled out after the 2026-09-09 demo, and it is
the one thing here a spreadsheet cannot do. Everything else in this script exists to set it up or
to prove it actually happened.

Runs about six minutes. Timings assume a freshly seeded database.

## Before you start

```powershell
# From the repo root. See docs/local-development.md for first-time setup.
docker compose up -d --wait database
dotnet run --project tools/PropFlow.Admin -- migrate
dotnet run --project tools/PropFlow.Admin -- configure-runtime
dotnet run --project tools/PropFlow.Admin -- seed-demo
```

Then the API and the client, in two more shells:

```powershell
$env:ASPNETCORE_URLS = 'https://localhost:5001'   # https, not http - see the note below
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/PropFlow.Api
```

```powershell
cd apps/web
$env:PROPFLOW_API_ORIGIN = 'https://localhost:5001'
npm run dev
```

Open `http://127.0.0.1:3000` and sign in as `tidewater-demo` /
`demo-admin@tidewater.example.test` with the `Demo__Password` you seeded with.

**The API must be on HTTPS.** The session and antiforgery cookies are `__Host-` prefixed and
`Secure`, so over plain http the antiforgery system throws and login fails with a 500. If the Next
dev server cannot verify the dev certificate, export it and point Node at it:
`dotnet dev-certs https --export-path <path>.pem --format PEM --no-password`, then set
`NODE_EXTRA_CA_CERTS` to that file.

**Re-seed between runs.** The demo assigns vendors to every `New` item in the org, which moves them
to `Assigned`. Running the script twice on one database leaves nothing to assign. `docker compose
down -v` and repeat the setup block.

## 1. The bulk assignment — the headline (2 min)

The whole point: one dispatcher assigning a vendor across a filtered queue in a few seconds,
with an audit trail per item.

1. Land on **Work**. Twelve items, every status and priority represented. Note the **Vendor**
   column — some rows carry a vendor's name, some read *Unassigned*.
2. Set **Status → New**. Four items, all unassigned. *"This is the morning queue: work that has
   come in and nobody owns yet."*
3. Tick the header checkbox. The toolbar shows **4 selected**.
4. **Assign vendor** → choose *Tidewater Pest Services* → **Continue**.
5. The confirm step names the vendor and the count before anything is written. **Confirm
   assignment**.
6. The summary reads **"Assigned 4 of 4 work items to Tidewater Pest Services."**

   Land on the counts — they are the point. If some of the selection already had that vendor the
   summary says so: *"Assigned 4 of 6 … (2 already had this vendor)."* The server reports what it
   actually changed rather than a success flag.

7. Clear the status filter. The four rows now read **Tidewater Pest Services** in the Vendor
   column.

**If asked "what if two people do this at once?"** — every item is sent with the row version it
was read at. If any one of them changed underneath you, the whole batch is refused with a 409 and
nothing is written; you reload and see the current state. It is all-or-nothing on purpose: a
half-applied bulk action is worse than a rejected one.

**If asked about completed work** — try it. Clear the filter, select a `Cancelled` row along with
a live one, and assign. The batch is refused: *"Completed and cancelled work cannot be assigned"*,
and the live item in the same batch is untouched. Terminal work is closed to assignment the same
way it is closed to status changes.

## 2. Finding the work to act on (1 min)

Bulk assignment is only useful over the right selection, so show the filters second, not first.

- **Search** — substring over title and description.
- **Status** and **Priority**.
- **Category** — from the organization's own category list, managed under **Categories**.
- Sortable **Title**, **Status**, **Priority**, **Due** columns; sorting is stable across pages.
- **Saved views** — name the current filter set, tick **Make this my default view**, save. It is
  labelled *(default)* and applies itself the next time the list opens. *"Every dispatcher lands
  on their own queue."*

## 3. The audit trail (1 min)

1. Click into one of the items just assigned.
2. Scroll to **Timeline**: a `VendorAssigned` entry, timestamped, with the previous and new vendor.
3. In **Scheduling & assignment**, pick an employee and **Assign employee**. Confirm.
4. The timeline gains an `EmployeeAssigned` entry beside the first.

*"Nothing here is editable after the fact — the timeline is append-only in the database, not just
in the UI."* The runtime role holds `SELECT, INSERT` on that table and a trigger rejects `UPDATE`
and `DELETE`, so an edit is refused at three separate layers.

## 4. Tenant isolation, if the audience cares (1 min)

Sign out, sign in as `isolation-demo` / `demo-admin@isolation.example.test` with the same
password. Same application, entirely different work. Isolation is enforced in the database with
row-level security rather than by a `WHERE` clause the application has to remember, and the
integration suite proves it with raw SQL that deliberately bypasses the ORM.

## What not to promise

- No mobile or field-technician view yet; the `Technician` and `Vendor` roles are deliberately
  denied all capabilities until assignment-level scoping exists.
- Resident communication exists as an outbox with templates (M4), but there is no messaging UI in
  this build.
- Bulk actions cover **vendor assignment only**. Employee, status and due-date are single-item
  operations today. If someone asks for those, that is a real signal — write it down.
