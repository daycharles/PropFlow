"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "../components/app-shell";
import { ProtectedPage } from "../components/protected-page";
import {
  api,
  ApiError,
  type AttentionItem,
  type AttentionQueue,
  type AttentionReason,
  type AttentionSeverity,
  type Session,
} from "../../lib/api";

const reasonLabels: Record<AttentionReason, string> = {
  UnassignedEmergency: "Unassigned emergency",
  SlaBreach: "SLA breach",
  Overdue: "Overdue",
  WaitingOnVendor: "Waiting on vendor",
  WaitingOnResident: "Waiting on resident",
  RepeatRepair: "Repeat repair",
  UnitTurnAtRisk: "Unit turn at risk",
};

const severities: { key: AttentionSeverity; label: string; blurb: string }[] = [
  {
    key: "Critical",
    label: "Critical",
    blurb: "Emergencies, SLA breaches and past-due critical work",
  },
  { key: "Warning", label: "Warning", blurb: "Overdue work, stalled vendors and repeat repairs" },
  { key: "Informational", label: "Informational", blurb: "Waiting on a resident" },
];

export default function AttentionPage() {
  return (
    <ProtectedPage capability="Work.Read">
      {(session) => <NeedsAttention session={session} />}
    </ProtectedPage>
  );
}

function NeedsAttention({ session }: { session: Session }) {
  const [queue, setQueue] = useState<AttentionQueue | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<AttentionSeverity | null>(null);

  async function load() {
    setLoading(true);
    setError("");
    try {
      setQueue(await api.attention.get());
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to load the attention queue.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, []);

  const counts: Record<AttentionSeverity, number> = {
    Critical: queue?.criticalCount ?? 0,
    Warning: queue?.warningCount ?? 0,
    Informational: queue?.informationalCount ?? 0,
  };
  const total = counts.Critical + counts.Warning + counts.Informational;
  const shown = queue
    ? filter
      ? queue.items.filter((item) => item.severity === filter)
      : queue.items
    : [];

  return (
    <AppShell session={session}>
      <section className="detail-workspace">
        <div className="work-heading">
          <div>
            <h1>Needs your attention</h1>
            <p>Open work that has tripped a rule, most urgent first.</p>
          </div>
          <button className="secondary" onClick={() => void load()} disabled={loading}>
            Refresh
          </button>
        </div>

        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}

        <div className="attention-cards" role="group" aria-label="Filter by severity">
          {severities.map((severity) => (
            <button
              key={severity.key}
              type="button"
              className={`attention-card severity-${severity.key.toLowerCase()}${
                filter === severity.key ? " active" : ""
              }`}
              aria-pressed={filter === severity.key}
              onClick={() =>
                setFilter((current) => (current === severity.key ? null : severity.key))
              }
            >
              <span className="attention-count">{counts[severity.key]}</span>
              <span className="attention-card-label">{severity.label}</span>
              <span className="attention-card-blurb">{severity.blurb}</span>
            </button>
          ))}
        </div>

        <section className="panel">
          <div className="attention-list-heading">
            <h2>
              {filter ? `${filter} items` : "All items"}
              {queue ? ` (${shown.length})` : ""}
            </h2>
            {filter ? (
              <button className="link-button" type="button" onClick={() => setFilter(null)}>
                Show all
              </button>
            ) : null}
          </div>

          {loading ? (
            <p>Loading…</p>
          ) : total === 0 ? (
            <p>Nothing needs your attention right now.</p>
          ) : shown.length === 0 ? (
            <p>No {filter?.toLowerCase()} items.</p>
          ) : (
            <ul className="attention-list">
              {shown.map((item) => (
                <AttentionRow key={`${item.workId}-${item.reason}`} item={item} />
              ))}
            </ul>
          )}
        </section>
      </section>
    </AppShell>
  );
}

function AttentionRow({ item }: { item: AttentionItem }) {
  return (
    <li className={`attention-row severity-${item.severity.toLowerCase()}`}>
      <div className="attention-row-main">
        <span className="attention-reason">{reasonLabels[item.reason]}</span>
        <Link href={`/work/${item.workId}`}>
          <strong>{item.title}</strong>
        </Link>
        <p className="attention-detail">{item.detail}</p>
      </div>
      <dl className="attention-row-meta">
        <div>
          <dt>Property</dt>
          <dd>{item.propertyName ?? "—"}</dd>
        </div>
        <div>
          <dt>Priority</dt>
          <dd>
            <span className={`priority ${priorityClass(item.priority)}`}>{item.priority}</span>
          </dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>
            <span className="badge">{item.status}</span>
          </dd>
        </div>
        <div>
          <dt>Due</dt>
          <dd>{formatDate(item.dueDate)}</dd>
        </div>
      </dl>
    </li>
  );
}

function priorityClass(priority: string) {
  return priority === "High" || priority === "Critical" ? "priority-urgent" : "";
}

function formatDate(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "—"
    : new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date);
}
