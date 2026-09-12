"use client";
import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  api,
  type ApplicationConsentLog,
  type ApplicationConsentType,
  type ApplicationDecision,
  type ApplicationStatus,
  type RecordedConsentDecision,
  type RentalApplication,
  type ScreeningRequestRow,
} from "../../lib/api";

/**
 * PF-S05.09 — the rental-application workflow for one application.
 *
 * This panel replaced the two buttons on /marketing/listings that drove the legacy
 * `PUT .../applicants/{id}/status` route. Those moved a person straight to Approved with no
 * consent check and no decision row, which is exactly what FS-S05 exists to stop; the route is
 * now fenced server-side (409 once a RentalApplication exists) so they would only have failed.
 *
 * Three rules the layout exists to make visible, not merely to obey:
 *
 *  1. Screening cannot start before consent. "Send to screening" is disabled until the API's own
 *     gate would pass, and `screeningGate` prints the reason next to it. An enabled button that
 *     409s is not a gate — it is a gate the user discovers by tripping over it.
 *  2. Approving over a `Fail` recommendation needs an override note. A Fail is deliberately not
 *     a hard block (individualized assessment), so the note is asked for up front and Approve
 *     stays disabled until it is typed, rather than the 409 being the first the user hears of it.
 *  3. A provider outage is a 503, not a verdict. It is rendered as a retryable transient in its
 *     own line, never as an error about the applicant and never as a Fail.
 */

const CONSENT_TYPES: ApplicationConsentType[] = [
  "BackgroundCheck",
  "CreditCheck",
  "EvictionHistory",
  "IncomeVerification",
];
const CONSENT_SOURCE = "Leasing agent (web)";
// Masked PII is ABSENT from the JSON, so the panel must never print "—" for it: "—" says the
// field is empty, and the truth is that this user may not see it.
const HIDDEN = "Hidden (needs Applications.ReadPii)";

type Props = {
  applicationId: string;
  canManage: boolean;
  onChanged?: () => void;
};

// The three reads that make up the panel: the application (with applicants and screening rows),
// the whole append-only consent history, and the decision trail.
type ApplicationBundle = {
  detail: RentalApplication;
  consent: ApplicationConsentLog;
  decisions: ApplicationDecision[];
};

async function fetchBundle(applicationId: string): Promise<ApplicationBundle> {
  const [detail, consent, decisions] = await Promise.all([
    api.applications.get(applicationId),
    api.applications.consent(applicationId),
    api.applications.decisions(applicationId),
  ]);
  return { detail, consent, decisions };
}

export function ApplicationPanel({ applicationId, canManage, onChanged }: Props) {
  const [application, setApplication] = useState<RentalApplication | null>(null);
  const [consent, setConsent] = useState<ApplicationConsentLog | null>(null);
  const [decisions, setDecisions] = useState<ApplicationDecision[]>([]);
  const [error, setError] = useState("");
  const [transient, setTransient] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [consentApplicantId, setConsentApplicantId] = useState("");
  const [consentType, setConsentType] = useState<ApplicationConsentType>("BackgroundCheck");
  const [consentDecision, setConsentDecision] = useState<RecordedConsentDecision>("Granted");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");

  const apply = (bundle: ApplicationBundle) => {
    setApplication(bundle.detail);
    setConsent(bundle.consent);
    setDecisions(bundle.decisions);
  };
  const load = async () => apply(await fetchBundle(applicationId));

  // The fetch is inlined and its state lands in a `.then`, which is the shape
  // `react-hooks/set-state-in-effect` accepts (same as components/repeat-repair-warning.tsx).
  // There are no synchronous resets either: the parent mounts this with `key={applicationId}`,
  // so switching applications remounts and every piece of state starts empty.
  useEffect(() => {
    let active = true;
    fetchBundle(applicationId)
      .then((bundle) => {
        if (!active) return;
        setApplication(bundle.detail);
        setConsent(bundle.consent);
        setDecisions(bundle.decisions);
      })
      .catch((caught: unknown) => {
        if (active)
          setError(caught instanceof Error ? caught.message : "Unable to load the application.");
      });
    return () => {
      active = false;
    };
  }, [applicationId]);

  // One place where an outage is told apart from a refusal. 503 means the provider could not
  // answer and nothing was written, so it is a transient the user can retry; everything else is
  // an error about this application.
  const run = async (action: () => Promise<unknown>, success: string) => {
    setError("");
    setTransient("");
    setNotice("");
    setBusy(true);
    try {
      await action();
      await load();
      setNotice(success);
      onChanged?.();
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 503) {
        setTransient(`${caught.message} Nothing was recorded — retry when the provider answers.`);
        // The attempt counter still moved, so the rows are reloaded even on the outage path.
        await load().catch(() => undefined);
        onChanged?.();
      } else {
        setError(caught instanceof Error ? caught.message : "The action could not be completed.");
      }
    } finally {
      setBusy(false);
    }
  };

  const granted = useMemo(() => {
    const byApplicant = new Map<string, ApplicationConsentType[]>();
    for (const row of consent?.effective ?? []) {
      if (row.decision !== "Granted") continue;
      byApplicant.set(row.applicantId, [
        ...(byApplicant.get(row.applicantId) ?? []),
        row.consentType,
      ]);
    }
    return byApplicant;
  }, [consent]);

  const applicants = application?.applicants ?? [];
  const everyoneConsented =
    applicants.length > 0 && applicants.every((row) => (granted.get(row.applicantId) ?? []).length);
  const failed = (application?.screening ?? []).some((row) => row.recommendation === "Fail");
  const gate = screeningGate(application?.status, applicants.length, everyoneConsented);
  const canScreen = canManage && gate === null && !busy;
  const canDecide = canManage && application?.status === "UnderReview";
  // A Fail does not block approval; it makes the override explicit. Deny always needs a reason.
  const approveBlocked = !reason.trim() || (failed && !note.trim());

  if (!application) {
    return (
      <section className="panel">
        <h3>Application</h3>
        {error ? (
          <p className="message" role="alert">
            {error}
          </p>
        ) : (
          <p>Loading application…</p>
        )}
      </section>
    );
  }

  return (
    <section className="panel">
      <h3>Application — {application.status}</h3>
      <p className="hint">
        {application.submittedAt
          ? `Submitted ${new Date(application.submittedAt).toLocaleString()}`
          : "Not submitted yet"}
        {application.decidedAt
          ? ` · Decided ${new Date(application.decidedAt).toLocaleString()}`
          : ""}
      </p>
      {notice && <p className="message success">{notice}</p>}
      {transient && (
        <p className="message" role="status">
          Screening provider unavailable. {transient}
        </p>
      )}
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}

      <h4>Applicants</h4>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Role</th>
              <th>Monthly income</th>
              <th>Employment</th>
              <th>Consent</th>
            </tr>
          </thead>
          <tbody>
            {applicants.map((row) => (
              <tr key={row.id}>
                <td>{row.name}</td>
                <td>{row.role}</td>
                {/* `in` rather than `?? "—"`: absent means masked, null means no value held. */}
                <td>
                  {"monthlyIncome" in row
                    ? row.monthlyIncome == null
                      ? "—"
                      : `$${row.monthlyIncome.toLocaleString()}`
                    : HIDDEN}
                </td>
                <td>{"employmentStatus" in row ? (row.employmentStatus ?? "—") : HIDDEN}</td>
                <td>{(granted.get(row.applicantId) ?? []).join(", ") || "No consent recorded"}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {!applicants.length && <p>No applicants on this application yet.</p>}
      </div>

      {canManage && applicants.length > 0 && (
        <form
          className="form-grid"
          onSubmit={(event) => {
            event.preventDefault();
            void run(
              () =>
                api.applications.recordConsent(application.id, {
                  applicantId: consentApplicantId || applicants[0].applicantId,
                  consentType,
                  decision: consentDecision,
                  source: CONSENT_SOURCE,
                }),
              `Consent ${consentDecision.toLowerCase()} recorded.`,
            );
          }}
        >
          <h4>Record consent</h4>
          <select
            aria-label="Consent applicant"
            value={consentApplicantId || applicants[0].applicantId}
            onChange={(event) => setConsentApplicantId(event.target.value)}
          >
            {applicants.map((row) => (
              <option key={row.applicantId} value={row.applicantId}>
                {row.name}
              </option>
            ))}
          </select>
          <select
            aria-label="Consent type"
            value={consentType}
            onChange={(event) => setConsentType(event.target.value as ApplicationConsentType)}
          >
            {CONSENT_TYPES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
          <select
            aria-label="Consent decision"
            value={consentDecision}
            onChange={(event) => setConsentDecision(event.target.value as RecordedConsentDecision)}
          >
            <option value="Granted">Granted</option>
            <option value="Revoked">Revoked</option>
          </select>
          <button type="submit" disabled={busy}>
            Record consent
          </button>
        </form>
      )}

      <h4>Screening</h4>
      {/* The reason the control is unavailable is printed, not left for the 409 to explain. */}
      {gate && <p className="hint">{gate}</p>}
      {canManage && (
        <button
          type="button"
          disabled={!canScreen}
          onClick={() =>
            void run(() => api.applications.startScreening(application.id), "Screening requested.")
          }
        >
          Send to screening
        </button>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Applicant</th>
              <th>Status</th>
              <th>Attempts</th>
              <th>Recommendation</th>
              <th>Detail</th>
              {canManage && <th>Action</th>}
            </tr>
          </thead>
          <tbody>
            {application.screening.map((row) => (
              <tr key={row.requestId}>
                <td>{nameOf(application, row.applicantId)}</td>
                <td>{row.status}</td>
                <td>{row.attempts}</td>
                <td>{row.recommendation ?? "Awaiting verdict"}</td>
                <td>{screeningDetail(row)}</td>
                {canManage && (
                  <td>
                    {row.status === "Pending" && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() =>
                          void run(
                            () => api.applications.retryScreening(application.id, row.requestId),
                            "Screening retried.",
                          )
                        }
                      >
                        Retry screening
                      </button>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        {!application.screening.length && <p>No screening requests yet.</p>}
      </div>

      {canDecide && (
        <div>
          <h4>Decision</h4>
          {failed && (
            <p className="message" role="alert">
              Screening recommends Fail. Approving is still allowed, but it requires an override
              note explaining the individualized assessment.
            </p>
          )}
          <div className="form-grid">
            <input
              aria-label="Decision reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Reason code (required)"
              required
            />
            <textarea
              aria-label={failed ? "Override note" : "Decision note"}
              value={note}
              onChange={(event) => setNote(event.target.value)}
              placeholder={failed ? "Override note (required)" : "Note (optional)"}
            />
            <button
              type="button"
              disabled={busy || approveBlocked}
              onClick={() =>
                void run(
                  () =>
                    api.applications.approve(application.id, {
                      reason: reason.trim(),
                      note: note.trim() || null,
                    }),
                  "Application approved.",
                )
              }
            >
              Approve application
            </button>
            <button
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() =>
                void run(
                  () =>
                    api.applications.deny(application.id, {
                      reason: reason.trim(),
                      note: note.trim() || null,
                    }),
                  "Application denied.",
                )
              }
            >
              Deny application
            </button>
          </div>
        </div>
      )}

      {/* Append-only, and shown unconditionally: a trail nobody can see is not a trail. */}
      <h4>Decision history</h4>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Outcome</th>
              <th>Reason</th>
              <th>Note</th>
              <th>Decided</th>
            </tr>
          </thead>
          <tbody>
            {decisions.map((decision) => (
              <tr key={decision.id}>
                <td>{decision.outcome}</td>
                <td>{decision.reason}</td>
                <td>{decision.note ?? "—"}</td>
                <td>{new Date(decision.decidedAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {!decisions.length && <p>No decisions recorded yet.</p>}
      </div>
    </section>
  );
}

// The gate text, in one place. null means the API would accept a screening request right now.
function screeningGate(
  status: ApplicationStatus | undefined,
  applicantCount: number,
  everyoneConsented: boolean,
): string | null {
  if (!status) return "Loading the application.";
  if (status === "Draft")
    return applicantCount
      ? "Submit the application before requesting screening."
      : "Add an applicant, then submit the application.";
  if (status === "Submitted")
    return everyoneConsented
      ? "Recording the consent will move this application to ConsentGranted."
      : "Every applicant must record consent before screening can start.";
  if (status === "Screening") return "Screening is in flight. Retry any request still pending.";
  if (status === "UnderReview") return "Screening is complete. Approve or deny below.";
  if (status === "ConsentGranted") return null;
  return `This application is ${status} and cannot be screened.`;
}

function screeningDetail(row: ScreeningRequestRow) {
  // `summary` absent = masked; `summary` null = the provider returned none.
  if (row.lastError) return `Last error: ${row.lastError}`;
  if (!("summary" in row)) return HIDDEN;
  const score = row.score == null ? "" : `Score ${row.score}. `;
  return `${score}${row.summary ?? "—"}`;
}

function nameOf(application: RentalApplication, applicantId: string) {
  return application.applicants.find((row) => row.applicantId === applicantId)?.name ?? applicantId;
}
