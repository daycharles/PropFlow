"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import { AppShell } from "../components/app-shell";
import { ProtectedPage } from "../components/protected-page";
import {
  api,
  ApiError,
  type IntegrationHealth,
  type IntegrationRecord,
  type IntegrationSource,
  type Session,
  type SyncReport,
} from "../../lib/api";

export default function IntegrationsPage() {
  return (
    <ProtectedPage capability="Integrations.Manage">
      {(session) => <Integrations session={session} />}
    </ProtectedPage>
  );
}

function connectionStatus(health: IntegrationHealth): { label: string; tone: string } {
  if (!health.isEnabled) return { label: "Disabled", tone: "muted" };
  if (health.consecutiveFailures > 0) return { label: "Failing", tone: "bad" };
  if (health.lastSucceededAt) return { label: "Healthy", tone: "good" };
  return { label: "Never synced", tone: "muted" };
}

function Integrations({ session }: { session: Session }) {
  const [connections, setConnections] = useState<IntegrationHealth[] | null>(null);
  const [sources, setSources] = useState<IntegrationSource[]>([]);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    setError("");
    try {
      const [list, catalog] = await Promise.all([
        api.integrations.list(),
        api.integrations.sources(),
      ]);
      setConnections(list);
      setSources(catalog);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to load integrations.");
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  return (
    <AppShell session={session}>
      <section className="detail-workspace">
        <div className="work-heading">
          <div>
            <h1>Integrations</h1>
            <p>Connected systems, their sync health, and records that need attention.</p>
          </div>
          <button className="secondary" onClick={() => void load()} disabled={connections === null}>
            Refresh
          </button>
        </div>

        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}

        {connections === null ? (
          <section className="panel">
            <p>Loading…</p>
          </section>
        ) : (
          <>
            {connections.length === 0 ? (
              <section className="panel">
                <p>No systems are connected yet.</p>
              </section>
            ) : (
              <div className="integration-list">
                {connections.map((connection) => (
                  <ConnectionCard key={connection.id} health={connection} onChanged={load} />
                ))}
              </div>
            )}
            <ConnectForm sources={sources} existing={connections} onConnected={load} />
          </>
        )}
      </section>
    </AppShell>
  );
}

function ConnectionCard({
  health,
  onChanged,
}: {
  health: IntegrationHealth;
  onChanged: () => Promise<void>;
}) {
  const status = connectionStatus(health);
  const [busy, setBusy] = useState(false);
  const [report, setReport] = useState<SyncReport | null>(null);
  const [actionError, setActionError] = useState("");
  const [records, setRecords] = useState<IntegrationRecord[] | null>(null);
  const [showRecords, setShowRecords] = useState(false);

  async function sync() {
    setBusy(true);
    setActionError("");
    setReport(null);
    try {
      setReport(await api.integrations.sync(health.id));
      await onChanged();
      if (showRecords) await loadRecords();
    } catch (cause) {
      setActionError(cause instanceof ApiError ? cause.message : "The sync could not run.");
    } finally {
      setBusy(false);
    }
  }

  async function toggleEnabled() {
    setBusy(true);
    setActionError("");
    try {
      await api.integrations.setEnabled(health.id, !health.isEnabled);
      await onChanged();
    } catch (cause) {
      setActionError(
        cause instanceof ApiError ? cause.message : "Could not change the connection.",
      );
    } finally {
      setBusy(false);
    }
  }

  const loadRecords = useCallback(async () => {
    try {
      const page = await api.integrations.records(health.id);
      setRecords(page.items);
    } catch {
      setRecords([]);
    }
  }, [health.id]);

  async function toggleRecords() {
    const next = !showRecords;
    setShowRecords(next);
    if (next && records === null) await loadRecords();
  }

  const unresolved = (records ?? []).filter((record) => record.syncState !== "Synced");

  return (
    <section className="panel integration-card">
      <div className="integration-card-head">
        <div>
          <h2>{health.displayName}</h2>
          <p className="hint">{health.sourceSystem}</p>
        </div>
        <span className={`badge status-${status.tone}`}>{status.label}</span>
      </div>

      <dl className="key-values">
        <dt>Last successful sync</dt>
        <dd title={health.lastSucceededAt ?? undefined}>{relative(health.lastSucceededAt)}</dd>
        <dt>Last attempt</dt>
        <dd title={health.lastAttemptedAt ?? undefined}>{relative(health.lastAttemptedAt)}</dd>
        <dt>Consecutive failures</dt>
        <dd>{health.consecutiveFailures}</dd>
        <dt>Tracked records</dt>
        <dd>{health.trackedRecords}</dd>
        <dt>Unresolved</dt>
        <dd>{health.failedRecords}</dd>
      </dl>

      {health.lastError ? (
        <p className="integration-error">Last error: {health.lastError}</p>
      ) : null}
      {actionError ? (
        <p className="message" role="alert">
          {actionError}
        </p>
      ) : null}
      {report ? (
        <p className="success" role="status">
          Sync {report.outcome.toLowerCase()}: saw {report.seen}, added {report.added}, updated{" "}
          {report.updated}, failed {report.failed}.
        </p>
      ) : null}

      <div className="integration-actions">
        <button onClick={() => void sync()} disabled={busy || !health.isEnabled}>
          {busy ? "Working…" : "Sync now"}
        </button>
        <button className="secondary" onClick={() => void toggleEnabled()} disabled={busy}>
          {health.isEnabled ? "Disable" : "Enable"}
        </button>
        {health.trackedRecords > 0 ? (
          <button className="link-button" type="button" onClick={() => void toggleRecords()}>
            {showRecords ? "Hide records" : "View records"}
          </button>
        ) : null}
      </div>

      {showRecords ? (
        <div className="integration-records">
          {records === null ? (
            <p>Loading records…</p>
          ) : records.length === 0 ? (
            <p>No records tracked yet.</p>
          ) : (
            <>
              {unresolved.length > 0 ? (
                <p className="hint">
                  {unresolved.length} of {records.length} record
                  {records.length === 1 ? "" : "s"} unresolved.
                </p>
              ) : null}
              <ul className="integration-record-list">
                {records.map((record) => (
                  <li key={record.id} className={`sync-${record.syncState.toLowerCase()}`}>
                    <span className="badge">{record.kind}</span>
                    <code>{record.externalId}</code>
                    <span className="integration-record-state">{record.syncState}</span>
                    {record.lastError ? <small>{record.lastError}</small> : null}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      ) : null}
    </section>
  );
}

function ConnectForm({
  sources,
  existing,
  onConnected,
}: {
  sources: IntegrationSource[];
  existing: IntegrationHealth[];
  onConnected: () => Promise<void>;
}) {
  const available = sources.filter(
    (source) => !existing.some((c) => c.sourceSystem === source.sourceSystem),
  );
  const [sourceSystem, setSourceSystem] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  if (available.length === 0) return null;

  async function connect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!sourceSystem || !displayName.trim()) return;
    setBusy(true);
    setError("");
    try {
      await api.integrations.create({ sourceSystem, displayName: displayName.trim() });
      setSourceSystem("");
      setDisplayName("");
      await onConnected();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Could not connect that system.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel">
      <h2>Connect a system</h2>
      <form className="integration-connect" onSubmit={connect}>
        <label>
          System
          <select value={sourceSystem} onChange={(event) => setSourceSystem(event.target.value)}>
            <option value="">Choose a system…</option>
            {available.map((source) => (
              <option key={source.sourceSystem} value={source.sourceSystem}>
                {source.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Name
          <input
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            placeholder="e.g. Yardi — East region"
          />
        </label>
        <button disabled={busy || !sourceSystem || !displayName.trim()}>
          {busy ? "Connecting…" : "Connect"}
        </button>
      </form>
      {error ? (
        <p className="message" role="alert">
          {error}
        </p>
      ) : null}
    </section>
  );
}

function relative(value?: string | null) {
  if (!value) return "never";
  const then = new Date(value).getTime();
  if (Number.isNaN(then)) return "never";
  const seconds = Math.round((Date.now() - then) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} hr ago`;
  const days = Math.round(hours / 24);
  return `${days} day${days === 1 ? "" : "s"} ago`;
}
