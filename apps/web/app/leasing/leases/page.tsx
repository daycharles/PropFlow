"use client";
import { useEffect, useMemo, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import {
  api,
  type Lease,
  type LeaseNotice,
  type LeaseParty,
  type LeaseCharge,
  type PortalLeaseDocument,
  type PropertyReference,
  type ResidentReference,
  type Session,
} from "../../../lib/api";
import { hasCapability } from "../../../lib/capabilities";

export default function LeasesPage() {
  return (
    <ProtectedPage capability="Work.Read">
      {(session) => <LeasesContent session={session} />}
    </ProtectedPage>
  );
}

function LeasesContent({ session }: { session: Session }) {
  const [leases, setLeases] = useState<Lease[]>([]);
  const [residents, setResidents] = useState<ResidentReference[]>([]);
  const [properties, setProperties] = useState<PropertyReference[]>([]);
  const [spaces, setSpaces] = useState<{ id: string; code: string; propertyId: string }[]>([]);
  const [residentId, setResidentId] = useState("");
  const [spaceId, setSpaceId] = useState("");
  const [startsOn, setStartsOn] = useState("2026-10-01");
  const [endsOn, setEndsOn] = useState("2027-09-30");
  const [rent, setRent] = useState("1650");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [noticeLease, setNoticeLease] = useState<Lease | null>(null);
  const [notices, setNotices] = useState<LeaseNotice[]>([]);
  const [partyLease, setPartyLease] = useState<Lease | null>(null);
  const [parties, setParties] = useState<LeaseParty[]>([]);
  const [partyName, setPartyName] = useState("");
  const [partyRole, setPartyRole] = useState("");
  const [partyEmail, setPartyEmail] = useState("");
  const [chargeLease, setChargeLease] = useState<Lease | null>(null);
  const [charges, setCharges] = useState<LeaseCharge[]>([]);
  const [documentLease, setDocumentLease] = useState<Lease | null>(null);
  const [leaseDocuments, setLeaseDocuments] = useState<PortalLeaseDocument[]>([]);
  const [documentTitle, setDocumentTitle] = useState("");
  const [documentUrl, setDocumentUrl] = useState("");
  const [documentResidentVisible, setDocumentResidentVisible] = useState(true);
  const [chargeDescription, setChargeDescription] = useState("");
  const [chargeAmount, setChargeAmount] = useState("");
  const [chargeDueOn, setChargeDueOn] = useState("");
  const [chargeType, setChargeType] = useState<"Recurring" | "OneTime">("Recurring");
  const canManage = hasCapability(session, "Leasing.Manage");
  const refresh = () =>
    Promise.all([api.leasing.leases.list(), api.residents.list(), api.properties.list()])
      .then(async ([leaseList, residentList, propertyList]) => {
        setLeases(leaseList);
        setResidents(residentList);
        setProperties(propertyList);
        const details = await Promise.all(
          propertyList.map((property) => api.properties.get(property.id)),
        );
        setSpaces(
          details.flatMap((detail) =>
            detail.spaces.map((space) => ({ ...space, propertyId: detail.property.id })),
          ),
        );
      })
      .catch(() => setError("Unable to load leases."));
  useEffect(() => {
    void refresh();
  }, []);
  const residentNames = useMemo(
    () => new Map(residents.map((resident) => [resident.id, resident.fullName])),
    [residents],
  );
  const spaceNames = useMemo(
    () => new Map(spaces.map((space) => [space.id, space.code])),
    [spaces],
  );
  const create = async (event: React.FormEvent) => {
    event.preventDefault();
    setError("");
    try {
      await api.leasing.leases.create({
        residentId,
        spaceId,
        startsOn,
        endsOn,
        monthlyRent: Number(rent),
      });
      await refresh();
      setMessage("Lease created.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to create lease.");
    }
  };
  const action = async (
    lease: Lease,
    kind: "activate" | "renew" | "notice" | "moveOut" | "transfer",
  ) => {
    try {
      if (kind === "activate") {
        await api.leasing.leases.activate(lease.id);
      } else if (kind === "renew") {
        const newEnd = window.prompt("Renewal end date (YYYY-MM-DD)", lease.endsOn);
        if (!newEnd) return;
        const newRent = window.prompt("Renewal monthly rent", String(lease.monthlyRent));
        if (!newRent) return;
        await api.leasing.leases.renew(lease.id, {
          endsOn: newEnd,
          monthlyRent: Number(newRent),
        });
      } else if (kind === "notice") {
        const today = new Date().toISOString().slice(0, 10);
        const noticeDate = window.prompt("Notice date (YYYY-MM-DD)", today);
        if (!noticeDate) return;
        const moveOutOn = window.prompt("Move-out date (YYYY-MM-DD)", lease.endsOn);
        if (!moveOutOn) return;
        await api.leasing.leases.notice(lease.id, {
          type: "MoveOut",
          noticeDate,
          moveOutOn,
        });
      } else if (kind === "moveOut") {
        await api.leasing.leases.moveOut(
          lease.id,
          lease.moveOutOn ?? new Date().toISOString().slice(0, 10),
        );
      } else {
        const targetResidentId = window.prompt("Target resident ID");
        if (!targetResidentId) return;
        const targetSpaceId = window.prompt("Target space ID");
        if (!targetSpaceId) return;
        await api.leasing.leases.transfer(lease.id, {
          residentId: targetResidentId,
          spaceId: targetSpaceId,
          effectiveOn:
            window.prompt("Effective date (YYYY-MM-DD)", new Date().toISOString().slice(0, 10)) ??
            "",
        });
      }
      await refresh();
      setMessage(
        kind === "activate"
          ? "Lease activated."
          : kind === "renew"
            ? "Lease renewed."
            : kind === "notice"
              ? "Move-out notice recorded."
              : kind === "transfer"
                ? "Lease transferred."
                : "Lease ended.",
      );
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to update lease.");
    }
  };
  const viewNotices = async (lease: Lease) => {
    setError("");
    try {
      setNoticeLease(lease);
      setNotices(await api.leasing.leases.notices(lease.id));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load lease notices.");
    }
  };
  const viewParties = async (lease: Lease) => {
    setError("");
    try {
      setPartyLease(lease);
      setParties(await api.leasing.leases.parties(lease.id));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load lease parties.");
    }
  };
  const addParty = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!partyLease) return;
    try {
      await api.leasing.leases.addParty(partyLease.id, {
        fullName: partyName,
        role: partyRole,
        email: partyEmail || null,
      });
      setPartyName("");
      setPartyRole("");
      setPartyEmail("");
      await viewParties(partyLease);
      setMessage("Lease party added.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to add lease party.");
    }
  };
  const viewCharges = async (lease: Lease) => {
    setError("");
    try {
      setChargeLease(lease);
      setCharges(await api.leasing.leases.charges(lease.id));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load lease charges.");
    }
  };
  const viewDocuments = async (lease: Lease) => {
    setError("");
    try {
      setDocumentLease(lease);
      setLeaseDocuments(await api.leasing.leases.documents(lease.id));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load lease documents.");
    }
  };
  const addDocument = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!documentLease) return;
    try {
      await api.leasing.leases.addDocument(documentLease.id, {
        title: documentTitle,
        documentUrl,
        residentVisible: documentResidentVisible,
      });
      setDocumentTitle("");
      setDocumentUrl("");
      await viewDocuments(documentLease);
      setMessage("Lease document added.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to add lease document.");
    }
  };
  const addCharge = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!chargeLease) return;
    try {
      await api.leasing.leases.addCharge(chargeLease.id, {
        type: chargeType,
        description: chargeDescription,
        amount: Number(chargeAmount),
        dueOn: chargeDueOn,
      });
      setChargeDescription("");
      setChargeAmount("");
      setChargeDueOn("");
      await viewCharges(chargeLease);
      setMessage("Charge added.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to add charge.");
    }
  };
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Leases</h1>
        <p>Manage resident agreements, renewals, notices, and move-out status.</p>
        {message && <p className="message">{message}</p>}
        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Resident</th>
                <th>Space</th>
                <th>Term</th>
                <th>Rent</th>
                <th>Status</th>
                {canManage && <th>Action</th>}
              </tr>
            </thead>
            <tbody>
              {leases.map((lease) => (
                <tr key={lease.id}>
                  <td>{residentNames.get(lease.residentId) ?? "—"}</td>
                  <td>{spaceNames.get(lease.spaceId) ?? "—"}</td>
                  <td>
                    {lease.startsOn} – {lease.endsOn}
                  </td>
                  <td>${lease.monthlyRent.toLocaleString()}</td>
                  <td>{lease.status}</td>
                  {canManage && (
                    <td>
                      <button type="button" onClick={() => void viewNotices(lease)}>
                        Notices
                      </button>{" "}
                      <button type="button" onClick={() => void viewParties(lease)}>
                        Parties
                      </button>{" "}
                      <button type="button" onClick={() => void viewCharges(lease)}>
                        Charges
                      </button>{" "}
                      <button type="button" onClick={() => void viewDocuments(lease)}>
                        Documents
                      </button>{" "}
                      {lease.status === "Draft" && (
                        <button type="button" onClick={() => void action(lease, "activate")}>
                          Activate
                        </button>
                      )}
                      {lease.status === "Active" && (
                        <>
                          <button type="button" onClick={() => void action(lease, "renew")}>
                            Renew
                          </button>{" "}
                          <button type="button" onClick={() => void action(lease, "notice")}>
                            Give move-out notice
                          </button>
                          <button type="button" onClick={() => void action(lease, "transfer")}>
                            Transfer
                          </button>
                        </>
                      )}
                      {lease.status === "Renewed" && (
                        <>
                          <button type="button" onClick={() => void action(lease, "notice")}>
                            Give move-out notice
                          </button>
                          <button type="button" onClick={() => void action(lease, "transfer")}>
                            Transfer
                          </button>
                        </>
                      )}
                      {lease.status === "NoticeGiven" && (
                        <button type="button" onClick={() => void action(lease, "moveOut")}>
                          Complete move-out
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
          {!leases.length && <p>No leases yet.</p>}
        </div>
      </section>
      {canManage && (
        <section className="panel">
          <h2>Create lease</h2>
          <form className="form-grid" onSubmit={(event) => void create(event)}>
            <select
              aria-label="Lease resident"
              value={residentId}
              onChange={(event) => setResidentId(event.target.value)}
              required
            >
              <option value="">Select resident</option>
              {residents.map((resident) => (
                <option key={resident.id} value={resident.id}>
                  {resident.fullName}
                </option>
              ))}
            </select>
            <select
              aria-label="Lease space"
              value={spaceId}
              onChange={(event) => setSpaceId(event.target.value)}
              required
            >
              <option value="">Select space</option>
              {spaces.map((space) => (
                <option key={space.id} value={space.id}>
                  {space.code}
                </option>
              ))}
            </select>
            <input
              aria-label="Lease start"
              type="date"
              value={startsOn}
              onChange={(event) => setStartsOn(event.target.value)}
              required
            />
            <input
              aria-label="Lease end"
              type="date"
              value={endsOn}
              onChange={(event) => setEndsOn(event.target.value)}
              required
            />
            <input
              aria-label="Lease monthly rent"
              type="number"
              min="0"
              value={rent}
              onChange={(event) => setRent(event.target.value)}
              required
            />
            <button type="submit">Create lease</button>
          </form>
        </section>
      )}
      {noticeLease && (
        <section className="panel">
          <h2>Notices for {residentNames.get(noticeLease.residentId) ?? "resident"}</h2>
          {notices.length ? (
            <ul>
              {notices.map((notice) => (
                <li key={notice.id}>
                  {notice.type} — due {notice.dueOn} — {notice.status}
                  {notice.notes ? ` — ${notice.notes}` : ""}
                </li>
              ))}
            </ul>
          ) : (
            <p>No notices recorded.</p>
          )}
        </section>
      )}
      {partyLease && (
        <section className="panel">
          <h2>Parties for {residentNames.get(partyLease.residentId) ?? "resident"}</h2>
          {parties.length ? (
            <ul>
              {parties.map((party) => (
                <li key={party.id}>
                  {party.fullName} — {party.role}
                  {party.email ? ` — ${party.email}` : ""}
                </li>
              ))}
            </ul>
          ) : (
            <p>No additional parties recorded.</p>
          )}
          {canManage && (
            <form className="form-grid" onSubmit={(event) => void addParty(event)}>
              <input
                aria-label="Lease party name"
                value={partyName}
                onChange={(event) => setPartyName(event.target.value)}
                placeholder="Full name"
                required
              />
              <input
                aria-label="Lease party role"
                value={partyRole}
                onChange={(event) => setPartyRole(event.target.value)}
                placeholder="Role"
                required
              />
              <input
                aria-label="Lease party email"
                type="email"
                value={partyEmail}
                onChange={(event) => setPartyEmail(event.target.value)}
              />
              <button type="submit">Add lease party</button>
            </form>
          )}
        </section>
      )}
      {chargeLease && (
        <section className="panel">
          <h2>Charges for {residentNames.get(chargeLease.residentId) ?? "resident"}</h2>
          {charges.length ? (
            <ul>
              {charges.map((charge) => (
                <li key={charge.id}>
                  {charge.description} — ${charge.amount.toLocaleString()} — due {charge.dueOn} —{" "}
                  {charge.status}
                </li>
              ))}
            </ul>
          ) : (
            <p>No charges recorded.</p>
          )}
          {canManage && (
            <form className="form-grid" onSubmit={(event) => void addCharge(event)}>
              <select
                aria-label="Charge type"
                value={chargeType}
                onChange={(event) => setChargeType(event.target.value as "Recurring" | "OneTime")}
              >
                <option value="Recurring">Recurring</option>
                <option value="OneTime">One-time</option>
              </select>
              <input
                aria-label="Charge description"
                value={chargeDescription}
                onChange={(event) => setChargeDescription(event.target.value)}
                placeholder="Description"
                required
              />
              <input
                aria-label="Charge amount"
                type="number"
                min="0.01"
                step="0.01"
                value={chargeAmount}
                onChange={(event) => setChargeAmount(event.target.value)}
                placeholder="Amount"
                required
              />
              <input
                aria-label="Charge due date"
                type="date"
                value={chargeDueOn}
                onChange={(event) => setChargeDueOn(event.target.value)}
                required
              />
              <button type="submit">Add charge</button>
            </form>
          )}
        </section>
      )}
      {documentLease && (
        <section className="panel">
          <h2>Documents for {residentNames.get(documentLease.residentId) ?? "resident"}</h2>
          {leaseDocuments.length ? (
            <ul>
              {leaseDocuments.map((document) => (
                <li key={document.id}>
                  <a href={document.documentUrl} target="_blank" rel="noreferrer">
                    {document.title}
                  </a>{" "}
                  — {document.status}
                  {document.status === "Draft" && (
                    <button
                      type="button"
                      onClick={async () => {
                        await api.leasing.leases.sendDocument(documentLease.id, document.id);
                        await viewDocuments(documentLease);
                      }}
                    >
                      Send
                    </button>
                  )}
                  {document.status === "Sent" && (
                    <button
                      type="button"
                      onClick={async () => {
                        const signedBy = window.prompt("Signed by", "Resident") ?? "";
                        if (!signedBy) return;
                        await api.leasing.leases.signDocument(
                          documentLease.id,
                          document.id,
                          signedBy,
                        );
                        await viewDocuments(documentLease);
                      }}
                    >
                      Record signature
                    </button>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p>No lease documents recorded.</p>
          )}
          {canManage && (
            <form className="form-grid" onSubmit={(event) => void addDocument(event)}>
              <input
                aria-label="Lease document title"
                value={documentTitle}
                onChange={(event) => setDocumentTitle(event.target.value)}
                placeholder="Document title"
                required
              />
              <input
                aria-label="Lease document URL"
                type="url"
                value={documentUrl}
                onChange={(event) => setDocumentUrl(event.target.value)}
                placeholder="https://..."
                required
              />
              <label>
                <input
                  type="checkbox"
                  checked={documentResidentVisible}
                  onChange={(event) => setDocumentResidentVisible(event.target.checked)}
                />{" "}
                Resident visible
              </label>
              <button type="submit">Add lease document</button>
            </form>
          )}
        </section>
      )}
    </AppShell>
  );
}
