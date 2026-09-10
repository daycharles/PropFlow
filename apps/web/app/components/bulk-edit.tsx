"use client";

import { useState } from "react";
import {
  api,
  ApiError,
  type BulkAssignmentResult,
  type Session,
  type WorkItem,
} from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";

type Action = "status" | "priority" | "schedule" | "note" | "reopen";
type Step = "form" | "confirm" | "working" | "done";

// Draft is not a valid bulk target (the endpoint rejects it); the rest are.
const statuses = ["New", "Assigned", "Scheduled", "InProgress", "OnHold", "Completed", "Cancelled"];
const priorities = ["Low", "Normal", "High", "Critical"];

const actionLabels: Record<Action, string> = {
  status: "Change status",
  priority: "Change priority",
  schedule: "Schedule",
  note: "Add a note",
  reopen: "Reopen",
};

/**
 * The remaining authorized bulk actions for a selected work batch (PF-5.12) — status, priority,
 * schedule, note and reopen — reusing the bounded, all-or-nothing `/api/work/bulk/*` endpoints
 * and their per-item concurrency tokens. Every one is a single transaction: it either applies to
 * the whole batch or nothing changes, so a rollback is shown as a failure, never partial success.
 */
export function BulkEditFlow({
  session,
  items,
  onClose,
}: {
  session: Session;
  items: WorkItem[];
  onClose: (reload: boolean) => void;
}) {
  const canEdit = hasCapability(session, "Work.Update");
  const [action, setAction] = useState<Action>("status");
  const [step, setStep] = useState<Step>("form");
  const [status, setStatus] = useState("");
  const [priority, setPriority] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [note, setNote] = useState("");
  const [noteInternal, setNoteInternal] = useState(true);
  const [error, setError] = useState("");
  const [result, setResult] = useState<BulkAssignmentResult | null>(null);

  const count = items.length;
  const noun = count === 1 ? "work item" : "work items";
  const endBeforeStart = Boolean(start) && Boolean(end) && new Date(end) < new Date(start);
  const trimmedNote = note.trim();

  const ready =
    count > 0 &&
    canEdit &&
    (action === "status"
      ? Boolean(status)
      : action === "priority"
        ? Boolean(priority)
        : action === "schedule"
          ? Boolean(start) && !endBeforeStart
          : action === "note"
            ? trimmedNote.length > 0 && trimmedNote.length <= 2000
            : true);

  const summary =
    action === "status"
      ? `Set status to ${status} on ${count} ${noun}.`
      : action === "priority"
        ? `Set priority to ${priority} on ${count} ${noun}.`
        : action === "schedule"
          ? `Schedule ${count} ${noun} for ${formatWindow(start, end)}. Every item must already have a vendor or employee.`
          : action === "note"
            ? `Add ${noteInternal ? "an internal" : "a resident-visible"} note to ${count} ${noun}.`
            : `Reopen ${count} ${noun}. Only completed or cancelled work can be reopened.`;

  async function run() {
    setError("");
    setStep("working");
    const workIds = items.map((item) => item.id);
    const concurrencyTokens = tokensFrom(items);
    try {
      let response: BulkAssignmentResult;
      if (action === "status") {
        response = await api.work.bulkStatus({ workIds, status, concurrencyTokens });
      } else if (action === "priority") {
        response = await api.work.bulkPriority({ workIds, priority, concurrencyTokens });
      } else if (action === "schedule") {
        response = await api.work.bulkSchedule({
          workIds,
          scheduledStart: new Date(start).toISOString(),
          scheduledEnd: end ? new Date(end).toISOString() : null,
          concurrencyTokens,
        });
      } else if (action === "note") {
        response = await api.work.bulkNote({
          workIds,
          note: trimmedNote,
          internal: noteInternal,
          concurrencyTokens,
        });
      } else {
        response = await api.work.bulkReopen({ workIds, concurrencyTokens });
      }
      setResult(response);
      setStep("done");
    } catch (cause) {
      // Every endpoint here is all-or-nothing: on any error the whole batch rolled back.
      setError(
        cause instanceof ApiError && cause.status === 409
          ? "Some selected work changed before this ran. Close this, refresh the list, and try again."
          : cause instanceof ApiError
            ? cause.message
            : "The bulk edit could not be completed. Nothing was changed.",
      );
      setStep("form");
    }
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="modal" role="dialog" aria-modal="true" aria-labelledby="bulk-edit-title">
        {step === "form" && (
          <>
            <h2 id="bulk-edit-title">Bulk edit</h2>
            {count === 0 ? (
              <p className="message" role="alert">
                None of the selected work is in the current list. Clear the filters or refresh, then
                try again.
              </p>
            ) : (
              <p>
                {count} {noun} selected.
              </p>
            )}
            {!canEdit && (
              <p className="message" role="alert">
                Your role cannot edit work items.
              </p>
            )}
            <label>
              Action
              <select
                value={action}
                onChange={(event) => {
                  setAction(event.target.value as Action);
                  setError("");
                }}
              >
                {(Object.keys(actionLabels) as Action[]).map((value) => (
                  <option key={value} value={value}>
                    {actionLabels[value]}
                  </option>
                ))}
              </select>
            </label>

            {action === "status" && (
              <label>
                New status
                <select value={status} onChange={(event) => setStatus(event.target.value)}>
                  <option value="">Choose a status</option>
                  {statuses.map((value) => (
                    <option key={value}>{value}</option>
                  ))}
                </select>
              </label>
            )}

            {action === "priority" && (
              <label>
                New priority
                <select value={priority} onChange={(event) => setPriority(event.target.value)}>
                  <option value="">Choose a priority</option>
                  {priorities.map((value) => (
                    <option key={value}>{value}</option>
                  ))}
                </select>
              </label>
            )}

            {action === "schedule" && (
              <fieldset className="flow-group">
                <legend>Visit window</legend>
                <label>
                  Start
                  <input
                    type="datetime-local"
                    value={start}
                    onChange={(event) => setStart(event.target.value)}
                  />
                </label>
                <label>
                  End (optional)
                  <input
                    type="datetime-local"
                    value={end}
                    min={start || undefined}
                    onChange={(event) => setEnd(event.target.value)}
                  />
                </label>
                {endBeforeStart && (
                  <p className="message" role="alert">
                    The end of the window must be after the start.
                  </p>
                )}
              </fieldset>
            )}

            {action === "note" && (
              <>
                <label>
                  Note
                  <textarea
                    value={note}
                    maxLength={2000}
                    onChange={(event) => setNote(event.target.value)}
                  />
                </label>
                <fieldset className="flow-group">
                  <legend>Visibility</legend>
                  <label className="inline-check">
                    <input
                      type="radio"
                      name="bulk-note-visibility"
                      checked={noteInternal}
                      onChange={() => setNoteInternal(true)}
                    />
                    Internal — staff only
                  </label>
                  <label className="inline-check">
                    <input
                      type="radio"
                      name="bulk-note-visibility"
                      checked={!noteInternal}
                      onChange={() => setNoteInternal(false)}
                    />
                    Resident-visible
                  </label>
                </fieldset>
              </>
            )}

            {action === "reopen" && (
              <p className="saved-views-note">
                Reopen moves completed or cancelled work back to an active state. Items that are not
                terminal make the whole batch fail.
              </p>
            )}

            {error && (
              <p className="message" role="alert">
                {error}
              </p>
            )}
            <div className="modal-actions">
              <button className="secondary" onClick={() => onClose(false)}>
                Cancel
              </button>
              <button disabled={!ready} onClick={() => setStep("confirm")}>
                Continue
              </button>
            </div>
          </>
        )}

        {step === "confirm" && (
          <>
            <h2 id="bulk-edit-title">Confirm</h2>
            <p>{summary}</p>
            <p className="saved-views-note">
              This is one transaction. If any item cannot take the change, nothing is applied.
            </p>
            <div className="modal-actions">
              <button className="secondary" onClick={() => setStep("form")}>
                Back
              </button>
              <button
                aria-label={`Confirm ${actionLabels[action].toLowerCase()}`}
                onClick={() => void run()}
              >
                Confirm
              </button>
            </div>
          </>
        )}

        {step === "working" && (
          <>
            <h2 id="bulk-edit-title">Working…</h2>
            <p role="status" aria-live="polite">
              Applying {actionLabels[action].toLowerCase()} to {count} {noun}.
            </p>
          </>
        )}

        {step === "done" && result && (
          <>
            <h2 id="bulk-edit-title">Done</h2>
            <p className="success" role="status">
              Applied to {result.changed} of {result.total} {result.total === 1 ? "item" : "items"}
              {result.unchanged > 0 ? ` — ${result.unchanged} already matched` : ""}. The list has
              been refreshed.
            </p>
            <div className="modal-actions">
              <button onClick={() => onClose(true)}>Done</button>
            </div>
          </>
        )}
      </section>
    </div>
  );
}

function tokensFrom(items: WorkItem[]) {
  return Object.fromEntries(
    items.filter((item) => item.rowVersion).map((item) => [item.id, item.rowVersion as string]),
  );
}

function formatWindow(start: string, end: string) {
  if (!start) return "an unset time";
  const fmt = (value: string) =>
    new Intl.DateTimeFormat(undefined, {
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    }).format(new Date(value));
  return end ? `${fmt(start)} – ${fmt(end)}` : fmt(start);
}
