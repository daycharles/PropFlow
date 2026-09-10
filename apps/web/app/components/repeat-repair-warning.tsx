"use client";

import { useEffect, useState } from "react";
import { api, ApiError, type RepeatRepairAssessment } from "../../lib/api";

const money = (value: number) =>
  new Intl.NumberFormat(undefined, { style: "currency", currency: "USD" }).format(value);

/**
 * The "Repeat Repair Warning" surface (PF-6.05). Fetches the asset's assessment against the
 * organization's repeat-repair policy and, only when it crosses the threshold, shows the repair
 * count, the total repair cost in the window and the asset age. Renders nothing while loading,
 * on error, or when the asset is not a repeat repair — it is an alert, not a status line.
 *
 * `compact` is the inline variant used next to the work detail's asset picker; the default is
 * the full-width banner on the asset page. The assessment already carries `ageInYears`; the
 * `ageInYears` prop is only a fallback for callers that have it in hand.
 */
export function RepeatRepairWarning({
  assetId,
  categoryId,
  ageInYears,
  compact = false,
}: {
  assetId: string;
  categoryId?: string | null;
  ageInYears?: number | null;
  compact?: boolean;
}) {
  // Keyed by the inputs it was fetched for, so a prop change never shows the previous asset's
  // result while the next request is in flight.
  const key = `${assetId}|${categoryId ?? ""}`;
  const [entry, setEntry] = useState<{ key: string; assessment: RepeatRepairAssessment } | null>(
    null,
  );

  useEffect(() => {
    let active = true;
    api.assets
      .repeatRepair(assetId, categoryId)
      .then((assessment) => {
        if (active) setEntry({ key: `${assetId}|${categoryId ?? ""}`, assessment });
      })
      .catch((cause) => {
        // A missing asset or a transient error simply suppresses the warning.
        if (!(cause instanceof ApiError)) throw cause;
      });
    return () => {
      active = false;
    };
  }, [assetId, categoryId]);

  const assessment = entry && entry.key === key ? entry.assessment : null;
  if (!assessment || !assessment.isRepeatRepair) return null;

  const age = assessment.ageInYears ?? ageInYears ?? null;
  const windowLabel =
    assessment.windowDays % 30 === 0
      ? `${assessment.windowDays / 30} months`
      : `${assessment.windowDays} days`;

  return (
    <div
      className={`repeat-repair-warning${compact ? " compact" : ""}`}
      role="alert"
      data-testid="repeat-repair-warning"
    >
      <h3>Repeat repair</h3>
      <p>
        {assessment.repairCount} repairs on this asset in the last {windowLabel}
        {assessment.matchByCategory && categoryId ? " for this category" : ""} — at or above the
        threshold of {assessment.repairThreshold}. Consider replacement.
      </p>
      <dl>
        <dt>Repairs</dt>
        <dt>Repair cost</dt>
        <dt>Asset age</dt>
        <dd>{assessment.repairCount}</dd>
        <dd>{money(assessment.totalCostInWindow)}</dd>
        <dd>{age != null ? `${age} ${age === 1 ? "year" : "years"}` : "—"}</dd>
      </dl>
    </div>
  );
}
