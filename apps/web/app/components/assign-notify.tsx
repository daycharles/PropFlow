"use client";

import { useEffect, useMemo, useState } from "react";
import {
  api,
  ApiError,
  type MessageTemplate,
  type Session,
  type Vendor,
  type WorkItem,
} from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";

type Step = "form" | "confirm" | "working" | "done";

type ScheduleOutcome =
  | { kind: "skipped" }
  | { kind: "done"; changed: number; total: number }
  | { kind: "failed"; message: string };

type NotifyOutcome =
  | { kind: "skipped" }
  | {
      kind: "done";
      queued: number;
      deduped: number;
      skipped: number;
      failed: number;
      total: number;
    };

type Outcome = {
  assignedChanged: number;
  assignedUnchanged: number;
  assignedTotal: number;
  vendorName: string;
  schedule: ScheduleOutcome;
  notify: NotifyOutcome;
};

/**
 * The PF-4.08 "assign & notify" flow: one vendor, one optional visit window, and one optional
 * resident message across a selection of work items — chosen, confirmed, then applied as
 * assign → schedule → notify, with a summary of what each step did.
 *
 * The three server steps are separate transactions. `bulk/vendor` and `bulk/schedule` are each
 * all-or-nothing; the per-item message sends are best-effort (a resident without consent is
 * skipped, not an error). Because assigning a vendor bumps every row's concurrency token, the
 * schedule step re-reads fresh tokens before it runs.
 */
export function AssignNotifyFlow({
  session,
  items,
  vendors,
  onClose,
}: {
  session: Session;
  items: WorkItem[];
  vendors: Vendor[];
  onClose: (reload: boolean) => void;
}) {
  const canSchedule = hasCapability(session, "Work.Update");
  const canNotify = hasCapability(session, "Communications.SendMessage");

  const [step, setStep] = useState<Step>("form");
  const [vendorId, setVendorId] = useState("");
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [notify, setNotify] = useState(false);
  const [templateId, setTemplateId] = useState("");
  const [templates, setTemplates] = useState<MessageTemplate[]>([]);
  const [templatesLoaded, setTemplatesLoaded] = useState(!canNotify);
  const [error, setError] = useState("");
  const [progress, setProgress] = useState("");
  const [outcome, setOutcome] = useState<Outcome | null>(null);

  useEffect(() => {
    if (!canNotify) return;
    let active = true;
    api.communication.templates
      .list()
      .then((list) => {
        if (active) setTemplates(list.filter((template) => template.isActive));
      })
      .catch(() => {
        // Templates are optional; on failure the notify section just stays unavailable.
      })
      .finally(() => {
        if (active) setTemplatesLoaded(true);
      });
    return () => {
      active = false;
    };
  }, [canNotify]);

  const vendor = vendors.find((entry) => entry.id === vendorId);
  const template = templates.find((entry) => entry.id === templateId);
  const count = items.length;
  const noun = count === 1 ? "work item" : "work items";
  const endBeforeStart = Boolean(start) && Boolean(end) && new Date(end) < new Date(start);
  const notifyReady = !notify || Boolean(templateId);
  const canContinue =
    count > 0 && Boolean(vendorId) && !endBeforeStart && !(end && !start) && notifyReady;

  const windowLabel = useMemo(() => formatWindow(start, end), [start, end]);

  async function run() {
    setError("");
    setStep("working");
    const ids = items.map((item) => item.id);

    let assigned;
    try {
      setProgress(`Assigning ${vendor?.name ?? "the vendor"}…`);
      assigned = await api.work.bulkAssignVendor({
        workIds: ids,
        vendorId,
        concurrencyTokens: tokensFrom(items),
      });
    } catch (cause) {
      // bulk/vendor is all-or-nothing: nothing changed, so returning to the form is safe.
      setError(
        cause instanceof ApiError && cause.status === 409
          ? "Some selected work changed before assignment. Close this, refresh the list, and try again."
          : cause instanceof ApiError
            ? cause.message
            : "The vendor assignment could not be completed.",
      );
      setStep("form");
      return;
    }

    let schedule: ScheduleOutcome = { kind: "skipped" };
    if (start) {
      setProgress("Scheduling the visit window…");
      try {
        const fresh = await Promise.all(ids.map((id) => api.work.get(id)));
        const result = await api.work.bulkSchedule({
          workIds: ids,
          scheduledStart: new Date(start).toISOString(),
          scheduledEnd: end ? new Date(end).toISOString() : null,
          concurrencyTokens: Object.fromEntries(fresh.map((w) => [w.id, String(w.version)])),
        });
        schedule = { kind: "done", changed: result.changed, total: result.total };
      } catch (cause) {
        schedule = {
          kind: "failed",
          message: cause instanceof ApiError ? cause.message : "Scheduling could not be completed.",
        };
      }
    }

    let notifyOutcome: NotifyOutcome = { kind: "skipped" };
    if (notify && templateId) {
      setProgress("Notifying residents…");
      const results = await Promise.allSettled(
        ids.map((id) => api.work.sendMessage(id, templateId)),
      );
      let queued = 0;
      let deduped = 0;
      let skipped = 0;
      let failed = 0;
      for (const result of results) {
        if (result.status === "fulfilled") {
          if (result.value.queued) queued += 1;
          else deduped += 1;
        } else if (result.reason instanceof ApiError && result.reason.status === 409) {
          skipped += 1;
        } else {
          failed += 1;
        }
      }
      notifyOutcome = { kind: "done", queued, deduped, skipped, failed, total: results.length };
    }

    setOutcome({
      assignedChanged: assigned.changed,
      assignedUnchanged: assigned.unchanged,
      assignedTotal: assigned.total,
      vendorName: vendor?.name ?? "the vendor",
      schedule,
      notify: notifyOutcome,
    });
    setStep("done");
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="assign-notify-title"
      >
        {step === "form" && (
          <>
            <h2 id="assign-notify-title">Assign &amp; notify</h2>
            <p>
              {count} {noun} selected.
            </p>
            <label>
              Vendor
              <select value={vendorId} onChange={(event) => setVendorId(event.target.value)}>
                <option value="">Choose a vendor</option>
                {vendors.map((entry) => (
                  <option key={entry.id} value={entry.id}>
                    {entry.name}
                  </option>
                ))}
              </select>
            </label>

            {canSchedule && (
              <fieldset className="flow-group">
                <legend>Visit window (optional)</legend>
                <label>
                  Visit start
                  <input
                    type="datetime-local"
                    value={start}
                    onChange={(event) => setStart(event.target.value)}
                  />
                </label>
                <label>
                  Visit end
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
                {end && !start && (
                  <p className="message" role="alert">
                    Set a start time for the window.
                  </p>
                )}
              </fieldset>
            )}

            {canNotify && (
              <fieldset className="flow-group">
                <legend>Notify residents (optional)</legend>
                {templatesLoaded && templates.length === 0 ? (
                  <p className="saved-views-note">No active message templates are available.</p>
                ) : (
                  <>
                    <label className="inline-check">
                      <input
                        type="checkbox"
                        checked={notify}
                        disabled={!templatesLoaded}
                        onChange={(event) => setNotify(event.target.checked)}
                      />
                      Send a message to residents about this visit
                    </label>
                    {notify && (
                      <label>
                        Template
                        <select
                          value={templateId}
                          onChange={(event) => setTemplateId(event.target.value)}
                        >
                          <option value="">Choose a template</option>
                          {templates.map((entry) => (
                            <option key={entry.id} value={entry.id}>
                              {entry.name} ({entry.channel === "Sms" ? "SMS" : "Email"})
                            </option>
                          ))}
                        </select>
                      </label>
                    )}
                    {notify && (
                      <p className="saved-views-note">
                        Residents who have not consented to that channel, or who have no contact
                        details, are skipped.
                      </p>
                    )}
                  </>
                )}
              </fieldset>
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
              <button disabled={!canContinue} onClick={() => setStep("confirm")}>
                Continue
              </button>
            </div>
          </>
        )}

        {step === "confirm" && (
          <>
            <h2 id="assign-notify-title">Confirm</h2>
            <ul className="flow-summary">
              <li>
                Assign <strong>{vendor?.name}</strong> to {count} {noun}.
              </li>
              {start && <li>Schedule the visit for {windowLabel}.</li>}
              {notify && template && (
                <li>
                  Send the <strong>{template.name}</strong>{" "}
                  {template.channel === "Sms" ? "SMS" : "email"} to residents who have consented.
                </li>
              )}
            </ul>
            <div className="modal-actions">
              <button className="secondary" onClick={() => setStep("form")}>
                Back
              </button>
              <button onClick={() => void run()}>Confirm</button>
            </div>
          </>
        )}

        {step === "working" && (
          <>
            <h2 id="assign-notify-title">Working…</h2>
            <p role="status" aria-live="polite">
              {progress}
            </p>
          </>
        )}

        {step === "done" && outcome && (
          <>
            <h2 id="assign-notify-title">Done</h2>
            <ul className="flow-summary">
              <li className="success">
                Assigned {outcome.vendorName} to {outcome.assignedChanged} of{" "}
                {outcome.assignedTotal} {outcome.assignedTotal === 1 ? "work item" : "work items"}
                {outcome.assignedUnchanged > 0
                  ? ` (${outcome.assignedUnchanged} already had this vendor)`
                  : ""}
                .
              </li>
              {outcome.schedule.kind === "done" && (
                <li className="success">
                  Scheduled {outcome.schedule.changed} of {outcome.schedule.total} for {windowLabel}
                  .
                </li>
              )}
              {outcome.schedule.kind === "failed" && (
                <li className="message">
                  The vendor was assigned, but scheduling did not complete:{" "}
                  {outcome.schedule.message} Retry scheduling from the list.
                </li>
              )}
              {outcome.notify.kind === "done" && (
                <li className={outcome.notify.failed > 0 ? "message" : "success"}>
                  {describeNotify(outcome.notify)}
                </li>
              )}
            </ul>
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

function describeNotify(notify: Extract<NotifyOutcome, { kind: "done" }>) {
  const parts = [`Queued ${notify.queued} of ${notify.total} resident messages`];
  if (notify.deduped > 0) parts.push(`${notify.deduped} already sent this hour`);
  if (notify.skipped > 0) parts.push(`${notify.skipped} skipped (no resident or consent)`);
  if (notify.failed > 0) parts.push(`${notify.failed} failed`);
  return `${parts.join(" · ")}.`;
}

function formatWindow(start: string, end: string) {
  if (!start) return "";
  const startDate = new Date(start);
  const dateTime = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(startDate);
  if (!end) return dateTime;
  const endDate = new Date(end);
  const sameDay = startDate.toDateString() === endDate.toDateString();
  const endText = new Intl.DateTimeFormat(undefined, {
    month: sameDay ? undefined : "short",
    day: sameDay ? undefined : "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(endDate);
  return `${dateTime} – ${endText}`;
}
