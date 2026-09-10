"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { FormEvent, useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { RepeatRepairWarning } from "../../components/repeat-repair-warning";
import {
  api,
  ApiError,
  type Asset,
  type Employee,
  type Session,
  type TimelineEntry,
  type Vendor,
  type WorkDetail,
} from "../../../lib/api";
import { hasCapability } from "../../../lib/capabilities";

const statuses = [
  "Draft",
  "New",
  "Assigned",
  "Scheduled",
  "InProgress",
  "OnHold",
  "Completed",
  "Cancelled",
];
const priorities = ["Low", "Normal", "High", "Critical"];

export default function WorkDetailPage() {
  const params = useParams<{ id: string }>();
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);
  useEffect(() => {
    void api
      .session()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setReady(true));
  }, []);
  if (!ready)
    return (
      <main className="centered">
        <p>Loading PropFlow…</p>
      </main>
    );
  if (!session)
    return (
      <main className="centered">
        <p>
          Your session has ended. <Link href="/">Sign in again</Link>.
        </p>
      </main>
    );
  return (
    <AppShell session={session} onLogout={() => setSession(null)}>
      <Detail session={session} id={params.id} />
    </AppShell>
  );
}

function Detail({ session, id }: { session: Session; id: string }) {
  const [work, setWork] = useState<WorkDetail | null>(null);
  const [timeline, setTimeline] = useState<TimelineEntry[]>([]);
  const [vendors, setVendors] = useState<Vendor[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const [confirm, setConfirm] = useState<"status" | "schedule" | "vendor" | "employee" | null>(
    null,
  );

  async function load() {
    setError("");
    try {
      const [item, entries, vendorList, employeeList] = await Promise.all([
        api.work.get(id),
        api.work.timeline(id),
        api.vendors.list(),
        api.employees.list(),
      ]);
      setWork(item);
      setTimeline(entries);
      setVendors(vendorList.filter((vendor) => vendor.isActive));
      setEmployees(employeeList.filter((employee) => employee.isActive));
      // The API rejects an asset at a different property, so only offer this property's assets.
      setAssets(item.propertyId ? await api.assets.list(item.propertyId) : []);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to load this work item.");
    }
  }
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
    // load intentionally uses the work id from this render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  async function save(event?: FormEvent<HTMLFormElement>) {
    event?.preventDefault();
    if (!work || !hasCapability(session, "Work.Update")) return;
    setSaving(true);
    setError("");
    try {
      const updated = await api.work.update(id, {
        title: work.title,
        description: work.description,
        categoryId: work.categoryId,
        priority: work.priority,
        propertyId: work.propertyId ?? "",
        buildingId: work.buildingId,
        spaceId: work.spaceId,
        residentId: work.residentId,
        assetId: work.assetId,
        dueDate: work.dueDate,
        cost: work.cost,
        internalNotes: work.internalNotes,
        residentVisibleNotes: work.residentVisibleNotes,
        status: work.status,
        scheduledStart: work.scheduledStart,
        scheduledEnd: work.scheduledEnd,
        version: work.version,
      });
      setWork(updated);
      setNotice("Saved");
      await load();
    } catch (cause) {
      setError(
        cause instanceof ApiError && cause.status === 409
          ? "This work item changed elsewhere. Reloaded the latest version."
          : cause instanceof ApiError
            ? cause.message
            : "Unable to save changes.",
      );
      if (cause instanceof ApiError && cause.status === 409) await load();
    } finally {
      setSaving(false);
    }
  }
  async function assignVendor() {
    if (!work?.vendorId || !hasCapability(session, "Work.AssignVendor")) return;
    setSaving(true);
    setError("");
    try {
      await api.work.assignVendor(id, work.vendorId, work.version);
      setNotice("Vendor assigned");
      await load();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to assign vendor.");
    } finally {
      setSaving(false);
      setConfirm(null);
    }
  }
  async function assignEmployee() {
    if (!work?.employeeId || !hasCapability(session, "Work.AssignEmployee")) return;
    setSaving(true);
    setError("");
    try {
      await api.work.assignEmployee(id, work.employeeId, work.version);
      setNotice("Employee assigned");
      await load();
    } catch (cause) {
      setError(
        cause instanceof ApiError && cause.status === 409
          ? "This work item changed elsewhere. Reloaded the latest version."
          : cause instanceof ApiError
            ? cause.message
            : "Unable to assign employee.",
      );
      if (cause instanceof ApiError && cause.status === 409) await load();
    } finally {
      setSaving(false);
      setConfirm(null);
    }
  }
  function change<K extends keyof WorkDetail>(key: K, value: WorkDetail[K]) {
    setWork((current) => (current ? { ...current, [key]: value } : current));
    setNotice("");
  }
  if (error && !work)
    return (
      <section className="panel">
        <p className="message">{error}</p>
        <Link href="/">Return to work</Link>
      </section>
    );
  if (!work)
    return (
      <section className="panel">
        <p>Loading work item…</p>
      </section>
    );
  const scheduleChanged = Boolean(work.scheduledStart);
  const canAssignEmployee = hasCapability(session, "Work.AssignEmployee");
  const isTechnician = session.role === "Technician";
  return (
    <section className={`detail-workspace${isTechnician ? " technician-workspace" : ""}`}>
      <div className="detail-heading">
        <div>
          <Link href="/">← Work</Link>
          <h1>{work.title}</h1>
          <p>
            {work.propertyName ?? "Property"} · {work.workType ?? "Work order"}
          </p>
        </div>
        <span className={`badge ${statusClass(work.status)}`}>{work.status}</span>
      </div>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {isTechnician && (
        <TechnicianQuickActions work={work} saving={saving} onChange={change} onSave={save} />
      )}
      <form className="detail-grid" onSubmit={save}>
        <section className="panel">
          <h2>Details</h2>
          <label>
            Title
            <input value={work.title} onChange={(event) => change("title", event.target.value)} />
          </label>
          <label>
            Description
            <textarea
              value={work.description ?? ""}
              onChange={(event) => change("description", event.target.value || null)}
            />
          </label>
          <div className="two-column">
            <label>
              Priority
              <select
                value={work.priority}
                onChange={(event) => change("priority", event.target.value)}
              >
                {priorities.map((value) => (
                  <option key={value}>{value}</option>
                ))}
              </select>
            </label>
            <label>
              Due date
              <input
                type="date"
                value={dateInput(work.dueDate)}
                onChange={(event) =>
                  change(
                    "dueDate",
                    event.target.value
                      ? new Date(`${event.target.value}T12:00:00`).toISOString()
                      : null,
                  )
                }
              />
            </label>
          </div>
          <label>
            Cost
            <input
              type="number"
              min="0"
              step="0.01"
              value={work.cost ?? ""}
              onChange={(event) =>
                change("cost", event.target.value === "" ? null : Number(event.target.value))
              }
            />
          </label>
          <label>
            Asset
            <select
              value={work.assetId ?? ""}
              onChange={(event) => change("assetId", event.target.value || null)}
            >
              <option value="">No asset</option>
              {assets.map((asset) => (
                <option key={asset.id} value={asset.id}>
                  {asset.name}
                </option>
              ))}
              {work.assetId && !assets.some((asset) => asset.id === work.assetId) ? (
                <option value={work.assetId}>Linked asset (not at this property)</option>
              ) : null}
            </select>
          </label>
          {work.assetId ? (
            <>
              <RepeatRepairWarning assetId={work.assetId} categoryId={work.categoryId} compact />
              <Link className="hint" href={`/assets/${work.assetId}`}>
                View asset maintenance history →
              </Link>
            </>
          ) : null}
          <button disabled={saving || !hasCapability(session, "Work.Update")}>
            {saving ? "Saving…" : "Save details"}
          </button>
        </section>
        <section className="panel">
          <h2>Scheduling & assignment</h2>
          <label>
            Vendor
            <select
              value={work.vendorId ?? ""}
              onChange={(event) => change("vendorId", event.target.value || null)}
            >
              <option value="">Unassigned</option>
              {vendors.map((vendor) => (
                <option key={vendor.id} value={vendor.id}>
                  {vendor.name}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="secondary"
            disabled={!work.vendorId || saving || !hasCapability(session, "Work.AssignVendor")}
            onClick={() => setConfirm("vendor")}
          >
            Assign vendor…
          </button>
          <label>
            Employee
            <select
              value={work.employeeId ?? ""}
              disabled={!canAssignEmployee}
              onChange={(event) => change("employeeId", event.target.value || null)}
            >
              <option value="">Unassigned</option>
              {employees.map((employee) => (
                <option key={employee.id} value={employee.id}>
                  {employee.displayName}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="secondary"
            disabled={!work.employeeId || saving || !canAssignEmployee}
            onClick={() => setConfirm("employee")}
          >
            Assign employee…
          </button>
          {!canAssignEmployee && <p className="hint">Your role cannot assign employees to work.</p>}
          <div className="two-column">
            <label>
              Start
              <input
                type="datetime-local"
                value={dateTimeInput(work.scheduledStart)}
                onChange={(event) =>
                  change(
                    "scheduledStart",
                    event.target.value ? new Date(event.target.value).toISOString() : null,
                  )
                }
              />
            </label>
            <label>
              End
              <input
                type="datetime-local"
                value={dateTimeInput(work.scheduledEnd)}
                onChange={(event) =>
                  change(
                    "scheduledEnd",
                    event.target.value ? new Date(event.target.value).toISOString() : null,
                  )
                }
              />
            </label>
          </div>
          <button
            type="button"
            className="secondary"
            disabled={!scheduleChanged || saving || !hasCapability(session, "Work.Update")}
            onClick={() => setConfirm("schedule")}
          >
            Confirm schedule…
          </button>
          <label>
            Status
            <select value={work.status} onChange={(event) => change("status", event.target.value)}>
              {statuses.map((value) => (
                <option key={value}>{value}</option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="secondary"
            disabled={saving || !hasCapability(session, "Work.Update")}
            onClick={() => setConfirm("status")}
          >
            Confirm status change…
          </button>
        </section>
        <section className="panel notes">
          <h2>Notes</h2>
          <p className="hint">
            Internal notes stay in the staff record. Only resident-visible notes are safe to share
            externally.
          </p>
          <label>
            Internal notes
            <textarea
              value={work.internalNotes ?? ""}
              onChange={(event) => change("internalNotes", event.target.value || null)}
              onBlur={() => void save()}
            />
          </label>
          <label>
            Resident-visible notes <span className="visibility-marker">Shared with resident</span>
            <textarea
              value={work.residentVisibleNotes ?? ""}
              onChange={(event) => change("residentVisibleNotes", event.target.value || null)}
              onBlur={() => void save()}
            />
          </label>
        </section>
      </form>
      <section className="panel timeline">
        <h2>Timeline</h2>
        {timeline.length ? (
          <ol>
            {timeline.map((entry) => (
              <li key={entry.id}>
                <strong>{entry.eventType ?? "Work updated"}</strong>
                {entry.residentVisible ? (
                  <span className="visibility-marker">Resident-visible</span>
                ) : null}
                <span>{formatDateTime(entry.occurredAt)}</span>
                {entry.communicationStatus ? (
                  <span
                    className={`badge communication-status communication-${entry.communicationStatus.toLowerCase()}`}
                  >
                    {entry.communicationStatus}
                  </span>
                ) : null}
                {entry.oldValue || entry.newValue ? (
                  <small>
                    {entry.oldValue ?? "—"} → {entry.newValue ?? "—"}
                  </small>
                ) : null}
              </li>
            ))}
          </ol>
        ) : (
          <p>No activity has been recorded yet.</p>
        )}
      </section>
      {confirm && (
        <div className="modal-backdrop">
          <section className="modal" role="dialog" aria-modal="true">
            <h2>Confirm change</h2>
            <p>
              {confirm === "vendor"
                ? "Assigning a vendor changes responsibility and records this in the timeline."
                : confirm === "employee"
                  ? "Assigning an employee changes responsibility and records this in the timeline."
                  : confirm === "schedule"
                    ? "Scheduling commits the selected time window."
                    : "Changing status updates the operational lifecycle and may be irreversible for completed work."}
            </p>
            <div className="modal-actions">
              <button className="secondary" onClick={() => setConfirm(null)}>
                Cancel
              </button>
              <button
                onClick={() => {
                  if (confirm === "vendor") void assignVendor();
                  else if (confirm === "employee") void assignEmployee();
                  else {
                    setConfirm(null);
                    void save();
                  }
                }}
              >
                {saving ? "Saving…" : "Confirm"}
              </button>
            </div>
          </section>
        </div>
      )}
    </section>
  );
}

function TechnicianQuickActions({
  work,
  saving,
  onChange,
  onSave,
}: {
  work: WorkDetail;
  saving: boolean;
  onChange: <K extends keyof WorkDetail>(key: K, value: WorkDetail[K]) => void;
  onSave: () => Promise<void>;
}) {
  return (
    <section className="panel technician-quick-actions" aria-label="Technician actions">
      <h2>Field update</h2>
      <p className="hint">Update the status or leave a note while you are on site.</p>
      <label>
        Status
        <select
          value={work.status}
          disabled={saving}
          onChange={(event) => onChange("status", event.target.value)}
        >
          {statuses
            .filter((status) => status !== "Draft")
            .map((status) => (
              <option key={status}>{status}</option>
            ))}
        </select>
      </label>
      <label>
        Work note
        <textarea
          value={work.internalNotes ?? ""}
          disabled={saving}
          placeholder="Add an internal progress note"
          onChange={(event) => onChange("internalNotes", event.target.value || null)}
        />
      </label>
      <div className="technician-action-row">
        <button type="button" disabled={saving} onClick={() => void onSave()}>
          {saving ? "Saving…" : "Save update"}
        </button>
        <label className="photo-action">
          <span>Add photo</span>
          <input type="file" accept="image/*" capture="environment" disabled />
        </label>
      </div>
      <p className="hint photo-note">
        Photo attachments will be available when work-media storage is enabled.
      </p>
    </section>
  );
}
function dateInput(value?: string | null) {
  return value ? value.slice(0, 10) : "";
}
/**
 * Format a stored UTC instant for an `<input type="datetime-local">`, which reads and writes
 * LOCAL wall-clock time and carries no offset.
 *
 * `toISOString().slice(0, 16)` was the defect: it emits UTC digits, so a scheduled visit stored
 * as 2026-09-15T13:00Z rendered as "13:00" in the input even at UTC-4, where the user had typed
 * 09:00. Worse than cosmetic — the onChange handlers at :366 and :379 parse a bare
 * `datetime-local` string as local and convert back with `toISOString()`, so re-confirming the
 * schedule the user was shown re-read those UTC digits as local and pushed the stored instant
 * forward by the offset on every pass.
 *
 * The local getters below are the exact inverse of that write path. Each one reports the local
 * wall-clock field of the instant, so the platform resolves the UTC offset — including a DST
 * change, where the offset differs from today's — and there is no `getTimezoneOffset()`
 * arithmetic whose sign can be inverted. Every field is zero-padded: `getMonth()` is 0-based and
 * the rest are unpadded numbers, and a local midnight must render as "00:00", not "0:0".
 */
function dateTimeInput(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const pad = (part: number, width = 2) => String(part).padStart(width, "0");
  const day = `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  return `${day}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(
    new Date(value),
  );
}
function statusClass(status: string) {
  return status === "Completed" ? "badge-complete" : status === "New" ? "badge-urgent" : "";
}
