"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import {
  ApiError,
  api,
  problemArray,
  type ActiveMember,
  type ConflictReason,
  type ConflictStatus,
  type ConflictsPage,
  type IntegrationConflict,
  type IntegrationEntityKind,
  type MappingIssue,
  type MappingProfile,
  type MappingSourceField,
  type Portfolio,
  type SyncRun,
} from "../../lib/api";

/**
 * PF-S19.09 — the three operator surfaces behind a connection: the conflict queue, the mapping
 * panel and the run history.
 *
 * The acceptance narrative is connect → sync → review conflicts → fix the mapping → promote →
 * sync → reconciled, and this file is where that story is either legible or is not. Three things
 * it exists to make true:
 *
 *  1. THE QUEUE DEFAULTS TO WHAT THE LATEST RUN STILL SEES. The reconciler never closes a
 *     conflict row — it re-observes one it still detects and stops observing one it cannot,
 *     because "I no longer detect it" and "a human decided" are different facts and only the
 *     second belongs in `resolvedByUserId`. The consequence is that after a successful fix the
 *     open rows are still there. Defaulting the view to `seenInLatestRun` and offering stale as
 *     a filter is how the fix becomes visible without auto-closing rows under a synthetic actor.
 *  2. A REFUSED PROMOTION SHOWS WHY. `promote` answers 409 carrying an `issues` array naming
 *     exactly what to fix. Rendering "conflict" instead would waste the entire mechanism.
 *  3. ReportOnly VERSUS AutoApply IS UNMISTAKABLE. In ReportOnly a sync raises conflicts and
 *     writes nothing to the operations schema. An operator who believes they are live when they
 *     are not is a support ticket.
 */

const ENTITY_KINDS: IntegrationEntityKind[] = [
  "Property",
  "Space",
  "Occupancy",
  "WorkOrder",
  "Asset",
  "Resident",
  "Building",
];
const CONFLICT_REASONS: ConflictReason[] = [
  "UnmappedValue",
  "MissingRequiredMapping",
  "MissingParentLink",
  "UpstreamDisappearance",
  "AmbiguousMatch",
  "ValidationRefusal",
];
const CONFLICT_STATUSES: ConflictStatus[] = ["Open", "Resolved", "Ignored"];

// Mirrors MappingVocabulary.AppliesTo (src/PropFlow.Domain/Integrations/MappingProfile.cs:52-58).
// Needed only for a kind that has NO profile yet, where the server has not sent
// `availableSourceFields`; once a profile exists the server's list is what is used.
const SOURCE_FIELDS_BY_KIND: Partial<Record<IntegrationEntityKind, MappingSourceField[]>> = {
  WorkOrder: ["WorkOrderStatus"],
  Asset: ["AssetKind"],
  Property: ["PropertyTimeZone"],
};

export type Freshness = "current" | "stale" | "all";
// The queue's view state lives in the parent card, not here. That is what lets the card remount a
// panel with `key={reloadToken}` after a sync — refreshing the data — without silently throwing
// away the filters the operator set. A `reloadToken` threaded through this component's own
// useCallback deps would do the same job while reading to the linter, correctly, as a dependency
// the callback never uses.
export type ConflictFilters = {
  status: ConflictStatus | "";
  kind: IntegrationEntityKind | "";
  reason: ConflictReason | "";
  freshness: Freshness;
};
export const DEFAULT_CONFLICT_FILTERS: ConflictFilters = {
  // Open, and only what the latest completed run still sees. Both halves are deliberate: a queue
  // that opens on every row ever raised is not a queue.
  status: "Open",
  kind: "",
  reason: "",
  freshness: "current",
};

// ---------------------------------------------------------------------------------------------
// Conflict queue
// ---------------------------------------------------------------------------------------------

export function ConflictQueue({
  connectionId,
  filters,
  onFilters,
  onChanged,
}: {
  connectionId: string;
  filters: ConflictFilters;
  onFilters: (filters: ConflictFilters) => void;
  onChanged: () => Promise<void>;
}) {
  const [page, setPage] = useState<ConflictsPage | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const { status, kind, reason, freshness } = filters;

  const load = useCallback(async () => {
    try {
      const result = await api.integrations.conflicts(connectionId, {
        status: status || undefined,
        kind: kind || undefined,
        reason: reason || undefined,
      });
      setPage(result);
      setError("");
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to load the conflict queue.");
    }
  }, [connectionId, status, kind, reason]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  async function close(conflict: IntegrationConflict, ignore: boolean) {
    setBusy(true);
    try {
      await api.integrations.closeConflict(connectionId, conflict.id, ignore);
      await load();
      await onChanged();
      setError("");
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "The conflict could not be closed.");
    } finally {
      setBusy(false);
    }
  }

  if (page === null) {
    return (
      <div className="integration-subpanel">
        <h3>Conflict queue</h3>
        {error ? (
          <p className="message" role="alert">
            {error}
          </p>
        ) : (
          <p>Loading conflicts…</p>
        )}
      </div>
    );
  }

  // The freshness split is applied over the page the server returned, because the conflicts route
  // filters on status/kind/reason only — `seenInLatestRun` is a per-row flag, not a query filter.
  // `openCount` and `staleCount` are the connection-wide totals and are what the counts line
  // reports, so paging never changes the numbers an operator is reading.
  const visible = page.items.filter((conflict) =>
    freshness === "all"
      ? true
      : freshness === "stale"
        ? !conflict.seenInLatestRun
        : conflict.seenInLatestRun,
  );

  return (
    <div className="integration-subpanel">
      <h3>Conflict queue</h3>
      {/* "showing N of M" rather than a bare M. With six Open conflicts of which two have gone
          stale, the status filter matches six while the freshness split renders four, and a line
          that said only "6 match the filter" over four visible rows reads as a bug. Seen the first
          time this panel met real data. */}
      <p className="hint" data-testid="conflict-counts">
        {page.openCount} open · {page.staleCount} no longer seen in the latest run · showing{" "}
        {visible.length} of {page.totalCount} matching the status, kind and reason filters
      </p>
      {page.staleCount > 0 && (
        <p className="hint" data-testid="stale-explainer">
          {page.staleCount} open conflict{page.staleCount === 1 ? "" : "s"} look fixed — the latest
          completed run did not detect {page.staleCount === 1 ? "it" : "them"} again. A sync never
          closes a conflict on its own: it stops re-observing one it can no longer see, and only a
          person can record a decision.
        </p>
      )}

      <div className="integration-filters">
        <label>
          Conflict status
          <select
            aria-label="Conflict status"
            value={status}
            onChange={(event) =>
              onFilters({ ...filters, status: event.target.value as ConflictStatus | "" })
            }
          >
            <option value="">Any status</option>
            {CONFLICT_STATUSES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
        <label>
          Conflict kind
          <select
            aria-label="Conflict kind"
            value={kind}
            onChange={(event) =>
              onFilters({ ...filters, kind: event.target.value as IntegrationEntityKind | "" })
            }
          >
            <option value="">Any kind</option>
            {ENTITY_KINDS.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
        <label>
          Conflict reason
          <select
            aria-label="Conflict reason"
            value={reason}
            onChange={(event) =>
              onFilters({ ...filters, reason: event.target.value as ConflictReason | "" })
            }
          >
            <option value="">Any reason</option>
            {CONFLICT_REASONS.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
        <label>
          Freshness
          <select
            aria-label="Conflict freshness"
            value={freshness}
            onChange={(event) =>
              onFilters({ ...filters, freshness: event.target.value as Freshness })
            }
          >
            <option value="current">Seen in the latest run</option>
            <option value="stale">No longer seen (looks fixed)</option>
            <option value="all">All</option>
          </select>
        </label>
      </div>

      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}

      {visible.length === 0 ? (
        <p data-testid="conflict-empty">
          {freshness === "current"
            ? "No conflicts were seen in the latest run."
            : "No conflicts match this filter."}
        </p>
      ) : (
        <ul className="conflict-list">
          {visible.map((conflict) => (
            <li
              key={conflict.id}
              className={`conflict-row ${conflict.seenInLatestRun ? "conflict-current" : "conflict-stale"}`}
            >
              <div className="conflict-head">
                <span className="badge">{conflict.kind}</span>
                <code>{conflict.externalId}</code>
                <span className="conflict-reason">{conflict.reason}</span>
                <span className="conflict-field">{conflict.field}</span>
                <span className={`badge status-${conflict.status === "Open" ? "bad" : "muted"}`}>
                  {conflict.status}
                </span>
                {!conflict.seenInLatestRun && (
                  <span className="badge status-good">Not seen in the latest run</span>
                )}
              </div>
              {conflict.detail && <p className="conflict-detail">{conflict.detail}</p>}
              <p className="hint">
                {conflict.observedValue != null && <>Source said “{conflict.observedValue}”. </>}
                {conflict.currentValue != null && <>PropFlow holds “{conflict.currentValue}”. </>}
                Seen {conflict.observationCount} time
                {conflict.observationCount === 1 ? "" : "s"}, last{" "}
                {new Date(conflict.lastSeenAt).toLocaleString()}.
              </p>
              {conflict.status === "Open" ? (
                <div className="integration-actions">
                  <button type="button" disabled={busy} onClick={() => void close(conflict, false)}>
                    Resolve
                  </button>
                  <button
                    type="button"
                    className="secondary"
                    disabled={busy}
                    onClick={() => void close(conflict, true)}
                  >
                    Ignore
                  </button>
                </div>
              ) : (
                <p className="hint">
                  {conflict.status}{" "}
                  {conflict.resolvedAt ? new Date(conflict.resolvedAt).toLocaleString() : ""}
                  {conflict.resolutionNote ? ` — ${conflict.resolutionNote}` : ""}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// Mapping panel
// ---------------------------------------------------------------------------------------------

export function MappingPanel({
  connectionId,
  kind,
  onKind,
  onChanged,
}: {
  connectionId: string;
  // Lifted into the card for the same reason the conflict filters are: the card remounts this
  // panel to refresh it after a sync, and the kind an operator was working on must survive that.
  kind: IntegrationEntityKind;
  onKind: (kind: IntegrationEntityKind) => void;
  onChanged: () => Promise<void>;
}) {
  const [profiles, setProfiles] = useState<MappingProfile[] | null>(null);
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [members, setMembers] = useState<ActiveMember[]>([]);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  // Issues from a REFUSED promotion, kept separate from the profile's own issue list so the panel
  // can say "this is why the promotion was refused just now" rather than only "this is wrong".
  const [refusal, setRefusal] = useState<MappingIssue[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const list = await api.integrations.mappings(connectionId);
      setProfiles(list);
      setError("");
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to load mapping profiles.");
      setProfiles([]);
    }
  }, [connectionId]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void load();
      // Lookups for the two id-valued defaults. Both are read-only and both are held by every
      // role that holds Integrations.Manage, but a failure here must not take the panel down —
      // the operator can still see the issues and the mode.
      void api.portfolios
        .list()
        .then(setPortfolios)
        .catch(() => setPortfolios([]));
      void api.members
        .list()
        .then(setMembers)
        .catch(() => setMembers([]));
    }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const profile = (profiles ?? []).find((candidate) => candidate.kind === kind);

  // Returns whether the write landed, so a form can clear itself on success and keep what the
  // operator typed on failure.
  async function run(action: () => Promise<unknown>, success: string): Promise<boolean> {
    setBusy(true);
    setError("");
    setNotice("");
    setRefusal([]);
    try {
      await action();
      await load();
      await onChanged();
      setNotice(success);
      return true;
    } catch (cause) {
      // A refused promotion is not a generic error: its body names each field to fix.
      const issues = problemArray<MappingIssue>(cause, "issues");
      if (issues.length) setRefusal(issues);
      setError(cause instanceof ApiError ? cause.message : "The mapping could not be saved.");
      return false;
    } finally {
      setBusy(false);
    }
  }

  if (profiles === null) {
    return (
      <div className="integration-subpanel">
        <h3>Mapping</h3>
        <p>Loading mapping profiles…</p>
      </div>
    );
  }

  const errors = (profile?.issues ?? []).filter((issue) => issue.severity === "Error");
  const unmapped = (profile?.issues ?? []).filter((issue) => issue.severity === "Unmapped");
  const sourceFields = profile?.availableSourceFields ?? SOURCE_FIELDS_BY_KIND[kind] ?? [];

  return (
    <div className="integration-subpanel">
      <h3>Mapping</h3>
      <div className="integration-filters">
        <label>
          Mapping kind
          <select
            aria-label="Mapping kind"
            value={kind}
            onChange={(event) => {
              onKind(event.target.value as IntegrationEntityKind);
              setRefusal([]);
              setNotice("");
              setError("");
            }}
          >
            {ENTITY_KINDS.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </label>
      </div>

      {/* The mode, said twice: as a badge and as the consequence. "ReportOnly" alone does not
          tell an operator that nothing is being written. */}
      <p data-testid="mapping-mode">
        <span className={`badge status-${profile?.mode === "AutoApply" ? "good" : "muted"}`}>
          {profile ? profile.mode : "Not configured"}
        </span>{" "}
        {profile?.mode === "AutoApply"
          ? "Auto-apply: a sync writes these records into PropFlow."
          : "Report only: a sync raises conflicts and writes nothing into PropFlow."}
      </p>

      {notice && <p className="success">{notice}</p>}
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      {refusal.length > 0 && (
        <div className="mapping-refusal" role="alert" data-testid="promotion-refusal">
          <p>Promotion was refused. Fix these first:</p>
          <ul>
            {refusal.map((issue) => (
              <li key={`${issue.severity}-${issue.field}`}>
                <strong>{issue.field}</strong> — {issue.reason}
              </li>
            ))}
          </ul>
        </div>
      )}

      <MappingDefaultsForm
        key={`${connectionId}-${kind}-${profile?.updatedAt ?? "new"}`}
        kind={kind}
        profile={profile}
        portfolios={portfolios}
        members={members}
        busy={busy}
        onSave={(input) =>
          run(
            () => api.integrations.saveMapping(connectionId, kind, input),
            "Mapping defaults saved.",
          )
        }
      />

      {errors.length > 0 && (
        <div className="mapping-issues mapping-issues-error" data-testid="mapping-errors">
          <h4>Errors — these block auto-apply</h4>
          <ul>
            {errors.map((issue) => (
              <li key={issue.field}>
                <strong>{issue.field}</strong> — {issue.reason}
              </li>
            ))}
          </ul>
        </div>
      )}
      {unmapped.length > 0 && (
        <div className="mapping-issues">
          <h4>Unmapped — imported into nothing, but not blocking</h4>
          <ul>
            {unmapped.map((issue) => (
              <li key={issue.field}>
                <strong>{issue.field}</strong> — {issue.reason}
              </li>
            ))}
          </ul>
        </div>
      )}

      {sourceFields.length > 0 && (
        <MappingRules
          key={kind}
          rules={profile?.rules ?? []}
          sourceFields={sourceFields}
          disabled={busy || !profile}
          onAdd={(input) =>
            run(
              () => api.integrations.addMappingRule(connectionId, kind, input),
              "Mapping rule added.",
            )
          }
          onRemove={(ruleId) =>
            run(
              () => api.integrations.removeMappingRule(connectionId, kind, ruleId),
              "Mapping rule removed.",
            )
          }
        />
      )}

      {profile && (
        <div className="integration-actions">
          {profile.mode === "AutoApply" ? (
            <button
              type="button"
              className="secondary"
              disabled={busy}
              onClick={() =>
                void run(
                  () => api.integrations.revertMappingToReportOnly(connectionId, kind),
                  "Back to report only. Syncs will raise conflicts and write nothing.",
                )
              }
            >
              Return to report only
            </button>
          ) : (
            <button
              type="button"
              disabled={busy}
              onClick={() =>
                void run(
                  () => api.integrations.promoteMapping(connectionId, kind),
                  "Promoted. Syncs will now write these records into PropFlow.",
                )
              }
            >
              Promote to auto-apply
            </button>
          )}
          {/* Deliberately NOT disabled on `canAutoApply === false`. The refusal, with its issue
              list, is more useful than a button that silently cannot be pressed. */}
          {profile.mode === "ReportOnly" && !profile.canAutoApply && (
            <span className="hint">
              This profile still has {errors.length} error{errors.length === 1 ? "" : "s"};
              promoting will say which.
            </span>
          )}
        </div>
      )}
    </div>
  );
}

function MappingDefaultsForm({
  kind,
  profile,
  portfolios,
  members,
  busy,
  onSave,
}: {
  kind: IntegrationEntityKind;
  profile?: MappingProfile;
  portfolios: Portfolio[];
  members: ActiveMember[];
  busy: boolean;
  onSave: (input: {
    targetPortfolioId: string | null;
    defaultCreatorId: string | null;
    defaultTimeZoneId: string | null;
  }) => Promise<boolean>;
}) {
  const [targetPortfolioId, setTargetPortfolioId] = useState(profile?.targetPortfolioId ?? "");
  const [defaultCreatorId, setDefaultCreatorId] = useState(profile?.defaultCreatorId ?? "");
  const [defaultTimeZoneId, setDefaultTimeZoneId] = useState(profile?.defaultTimeZoneId ?? "");

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void onSave({
      targetPortfolioId: targetPortfolioId || null,
      defaultCreatorId: defaultCreatorId || null,
      defaultTimeZoneId: defaultTimeZoneId.trim() || null,
    });
  }

  return (
    <form className="integration-connect" onSubmit={submit}>
      <label>
        Target portfolio
        <select
          aria-label="Target portfolio"
          value={targetPortfolioId}
          onChange={(event) => setTargetPortfolioId(event.target.value)}
        >
          <option value="">Not set</option>
          {portfolios.map((portfolio) => (
            <option key={portfolio.id} value={portfolio.id}>
              {portfolio.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Default creator
        <select
          aria-label="Default creator"
          value={defaultCreatorId}
          onChange={(event) => setDefaultCreatorId(event.target.value)}
        >
          <option value="">Not set</option>
          {members.map((member) => (
            <option key={member.userId} value={member.userId}>
              {member.email}
            </option>
          ))}
        </select>
      </label>
      <label>
        Default time zone
        <input
          aria-label="Default time zone"
          value={defaultTimeZoneId}
          onChange={(event) => setDefaultTimeZoneId(event.target.value)}
          placeholder="America/New_York"
        />
      </label>
      <button disabled={busy}>{profile ? "Save mapping" : `Create ${kind} mapping`}</button>
    </form>
  );
}

function MappingRules({
  rules,
  sourceFields,
  disabled,
  onAdd,
  onRemove,
}: {
  rules: MappingProfile["rules"];
  sourceFields: MappingSourceField[];
  disabled: boolean;
  onAdd: (input: {
    sourceField: MappingSourceField;
    sourceValue: string;
    targetValue: string;
  }) => Promise<boolean>;
  onRemove: (ruleId: string) => Promise<boolean>;
}) {
  const [sourceField, setSourceField] = useState<MappingSourceField>(sourceFields[0]);
  const [sourceValue, setSourceValue] = useState("");
  const [targetValue, setTargetValue] = useState("");

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!sourceValue.trim() || !targetValue.trim()) return;
    void onAdd({
      sourceField,
      sourceValue: sourceValue.trim(),
      targetValue: targetValue.trim(),
    }).then((saved) => {
      // Only on success: a refused add keeps what was typed so it can be corrected.
      if (!saved) return;
      setSourceValue("");
      setTargetValue("");
    });
  }

  return (
    <div className="mapping-rules">
      <h4>Value rules</h4>
      {rules.length === 0 ? (
        <p className="hint">No rules yet. The reconciler never guesses an unmapped value.</p>
      ) : (
        <ul className="integration-record-list">
          {rules.map((rule) => (
            <li key={rule.id}>
              <span className="badge">{rule.sourceField}</span>
              <code>{rule.sourceValue}</code>
              <span aria-hidden="true">→</span>
              <code>{rule.targetValue}</code>
              <button
                type="button"
                className="link-button"
                disabled={disabled}
                onClick={() => void onRemove(rule.id)}
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
      <form className="integration-connect" onSubmit={submit}>
        <label>
          Source field
          <select
            aria-label="Rule source field"
            value={sourceField}
            onChange={(event) => setSourceField(event.target.value as MappingSourceField)}
          >
            {sourceFields.map((field) => (
              <option key={field} value={field}>
                {field}
              </option>
            ))}
          </select>
        </label>
        <label>
          Source value
          <input
            aria-label="Rule source value"
            value={sourceValue}
            onChange={(event) => setSourceValue(event.target.value)}
            placeholder="e.g. Open"
          />
        </label>
        <label>
          PropFlow value
          <input
            aria-label="Rule target value"
            value={targetValue}
            onChange={(event) => setTargetValue(event.target.value)}
            placeholder="e.g. New"
          />
        </label>
        <button disabled={disabled || !sourceValue.trim() || !targetValue.trim()}>Add rule</button>
      </form>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// Run history
// ---------------------------------------------------------------------------------------------

export function RunHistory({ connectionId }: { connectionId: string }) {
  const [runs, setRuns] = useState<SyncRun[] | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void api.integrations
        .runs(connectionId)
        .then((result) => setRuns(result.items))
        .catch((cause: unknown) => {
          setRuns([]);
          setError(cause instanceof ApiError ? cause.message : "Unable to load the run history.");
        });
    }, 0);
    return () => window.clearTimeout(timer);
  }, [connectionId]);

  if (runs === null) {
    return (
      <div className="integration-subpanel">
        <h3>Sync history</h3>
        <p>Loading runs…</p>
      </div>
    );
  }

  return (
    <div className="integration-subpanel">
      <h3>Sync history</h3>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      {runs.length === 0 ? (
        <p>No runs yet.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Started</th>
                <th>Trigger</th>
                <th>Attempt</th>
                <th>Status</th>
                <th>Seen</th>
                <th>Added</th>
                <th>Updated</th>
                <th>Failed</th>
                <th>Conflicted</th>
                <th>Error</th>
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr key={run.id}>
                  <td>{new Date(run.startedAt).toLocaleString()}</td>
                  <td>{run.trigger}</td>
                  <td>{run.attemptNumber}</td>
                  <td>{run.status}</td>
                  <td>{run.seen}</td>
                  <td>{run.added}</td>
                  <td>{run.updated}</td>
                  <td>{run.failed}</td>
                  <td>{run.conflicted}</td>
                  <td>{run.error ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
