"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import { RepeatRepairWarning } from "../../components/repeat-repair-warning";
import { api, ApiError, type AssetHistory, type Session } from "../../../lib/api";

const kindLabels: Record<string, string> = {
  Other: "Other",
  Hvac: "HVAC",
  WaterHeater: "Water heater",
  Appliance: "Appliance",
  Roof: "Roof",
  ElectricalPanel: "Electrical panel",
  PlumbingFixture: "Plumbing fixture",
  Generator: "Generator",
};

export default function AssetDetailPage() {
  const params = useParams<{ id: string }>();
  return (
    <ProtectedPage capability="Work.Read">
      {(session) => <AssetDetail session={session} id={params.id} />}
    </ProtectedPage>
  );
}

function AssetDetail({ session, id }: { session: Session; id: string }) {
  const [data, setData] = useState<AssetHistory | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    api.assets
      .history(id)
      .then((result) => {
        if (active) setData(result);
      })
      .catch((cause) => {
        if (active)
          setError(
            cause instanceof ApiError && cause.status === 404
              ? "That asset could not be found."
              : cause instanceof ApiError
                ? cause.message
                : "Unable to load this asset.",
          );
      });
    return () => {
      active = false;
    };
  }, [id]);

  if (error)
    return (
      <AppShell session={session}>
        <section className="panel">
          <p className="message">{error}</p>
          <Link href="/">Return to work</Link>
        </section>
      </AppShell>
    );
  if (!data)
    return (
      <AppShell session={session}>
        <section className="panel">
          <p>Loading asset…</p>
        </section>
      </AppShell>
    );

  const { asset } = data;
  return (
    <AppShell session={session}>
      <section className="detail-workspace">
        <div className="detail-heading">
          <div>
            <Link href="/">← Work</Link>
            <h1>{asset.name}</h1>
            <p>
              {kindLabels[asset.kind] ?? asset.kind}
              {data.ageInYears != null
                ? ` · ${data.ageInYears} ${data.ageInYears === 1 ? "year" : "years"} old`
                : ""}
            </p>
          </div>
          <span className="badge">{asset.condition}</span>
        </div>

        <RepeatRepairWarning assetId={asset.id} ageInYears={data.ageInYears} />

        <div className="detail-grid">
          <section className="panel">
            <h2>Asset</h2>
            <dl className="key-values">
              <Field label="Manufacturer" value={asset.manufacturer} />
              <Field label="Model" value={asset.model} />
              <Field label="Serial number" value={asset.serialNumber} />
              <Field label="Installed" value={formatDate(asset.installedOn)} />
              <Field
                label="Warranty"
                value={
                  asset.warrantyExpiresOn
                    ? `${formatDate(asset.warrantyExpiresOn)}${data.underWarranty ? " (active)" : " (expired)"}`
                    : "—"
                }
              />
              <Field
                label="Expected service life"
                value={
                  asset.expectedServiceLifeYears != null
                    ? `${asset.expectedServiceLifeYears} years`
                    : "—"
                }
              />
              <Field label="Replacement cost" value={formatMoney(asset.replacementCostEstimate)} />
            </dl>
            {asset.notes ? <p className="hint">{asset.notes}</p> : null}
          </section>

          <section className="panel">
            <h2>Maintenance so far</h2>
            <dl className="key-values">
              <Field label="Work orders" value={String(data.workOrderCount)} />
              <Field label="Total logged cost" value={formatMoney(data.totalCost)} />
              <Field
                label="Age"
                value={data.ageInYears != null ? `${data.ageInYears} years` : "unknown"}
              />
            </dl>
          </section>
        </div>

        <section className="panel">
          <h2>Maintenance history</h2>
          {data.history.length === 0 ? (
            <p>No work has been linked to this asset yet.</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Work</th>
                    <th>Status</th>
                    <th>Category</th>
                    <th>Vendor</th>
                    <th>Reported</th>
                    <th>Completed</th>
                    <th>Cost</th>
                  </tr>
                </thead>
                <tbody>
                  {data.history.map((item) => (
                    <tr key={item.id}>
                      <td data-label="Work">
                        <Link href={`/work/${item.id}`}>
                          <strong>{item.title}</strong>
                        </Link>
                      </td>
                      <td data-label="Status">
                        <span className="badge">{item.status}</span>
                      </td>
                      <td data-label="Category">{item.categoryName ?? "—"}</td>
                      <td data-label="Vendor">{item.vendorName ?? "—"}</td>
                      <td data-label="Reported">{formatDate(item.createdAt)}</td>
                      <td data-label="Completed">{formatDate(item.completedAt)}</td>
                      <td data-label="Cost">{formatMoney(item.cost)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </section>
    </AppShell>
  );
}

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{value && value.length ? value : "—"}</dd>
    </>
  );
}

function formatDate(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value.length <= 10 ? `${value}T00:00:00` : value);
  return Number.isNaN(date.getTime())
    ? "—"
    : new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date);
}

function formatMoney(value?: number | null) {
  return value == null
    ? "—"
    : new Intl.NumberFormat(undefined, { style: "currency", currency: "USD" }).format(value);
}
