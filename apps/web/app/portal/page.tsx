"use client";
import { useEffect, useState } from "react";
import { AppShell } from "../components/app-shell";
import { ProtectedPage } from "../components/protected-page";
import {
  api,
  type PortalAnnouncement,
  type PortalDocument,
  type PortalLeaseDocument,
  type PortalSummary,
  type LeaseCharge,
  type ResidentPayment,
  type Session,
} from "../../lib/api";

export default function ResidentPortalPage() {
  return (
    <ProtectedPage capability="ResidentPortal.Read">
      {(session) => <PortalContent session={session} />}
    </ProtectedPage>
  );
}

function PortalContent({ session }: { session: Session }) {
  const [summary, setSummary] = useState<PortalSummary | null>(null);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [emailEnabled, setEmailEnabled] = useState(false);
  const [smsEnabled, setSmsEnabled] = useState(false);
  const [memberName, setMemberName] = useState("");
  const [memberRelationship, setMemberRelationship] = useState("");
  const [memberEmail, setMemberEmail] = useState("");
  const [documents, setDocuments] = useState<PortalDocument[]>([]);
  const [leaseDocuments, setLeaseDocuments] = useState<PortalLeaseDocument[]>([]);
  const [announcements, setAnnouncements] = useState<PortalAnnouncement[]>([]);
  const [payments, setPayments] = useState<ResidentPayment[]>([]);
  const [charges, setCharges] = useState<LeaseCharge[]>([]);
  const [paymentAmount, setPaymentAmount] = useState("");
  const [paymentDueOn, setPaymentDueOn] = useState("");
  const [paymentReference, setPaymentReference] = useState("");
  const [paymentChargeId, setPaymentChargeId] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const refresh = () =>
    Promise.all([
      api.portal.me(),
      api.portal.documents(),
      api.portal.leaseDocuments(),
      api.portal.payments(),
      api.portal.charges(),
      api.portal.announcements(),
    ])
      .then(
        ([data, documentList, leaseDocumentList, paymentList, chargeList, announcementList]) => {
          setSummary(data);
          setFullName(data.resident.fullName);
          setEmail(data.resident.email ?? "");
          setPhone(data.resident.phone ?? "");
          setEmailEnabled(data.resident.emailConsent === "Granted");
          setSmsEnabled(data.resident.smsConsent === "Granted");
          setDocuments(documentList);
          setLeaseDocuments(leaseDocumentList);
          setPayments(paymentList);
          setCharges(chargeList);
          setAnnouncements(announcementList);
        },
      )
      .catch(() => setError("Unable to load your resident portal."));
  useEffect(() => {
    void refresh();
  }, []);
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!summary?.occupancy) return;
    setError("");
    try {
      await api.portal.createServiceRequest({
        spaceId: summary.occupancy.spaceId,
        title,
        description,
      });
      setTitle("");
      setDescription("");
      await refresh();
      setMessage("Service request submitted.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to submit request.");
    }
  };
  const submitPayment = async (event: React.FormEvent) => {
    event.preventDefault();
    const selectedCharge = charges.find((charge) => charge.id === paymentChargeId);
    const lease =
      summary?.leases.find((item) => item.id === selectedCharge?.leaseId) ?? summary?.leases[0];
    if (!lease) return;
    try {
      await api.portal.submitPayment({
        leaseId: lease.id,
        chargeId: paymentChargeId || null,
        amount: Number(paymentAmount),
        dueOn: paymentDueOn,
        reference: paymentReference || null,
      });
      setPaymentAmount("");
      setPaymentDueOn("");
      setPaymentReference("");
      setPaymentChargeId("");
      await refresh();
      setMessage("Payment submitted.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to submit payment.");
    }
  };
  const saveProfile = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await api.portal.updateProfile({ fullName, email: email || null, phone: phone || null });
      await refresh();
      setMessage("Profile updated.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to update your profile.");
    }
  };
  const savePreferences = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await api.portal.updatePreferences({ emailEnabled, smsEnabled });
      await refresh();
      setMessage("Communication preferences updated.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to update preferences.");
    }
  };
  const addHouseholdMember = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await api.portal.createHouseholdMember({
        fullName: memberName,
        relationship: memberRelationship,
        email: memberEmail || null,
      });
      setMemberName("");
      setMemberRelationship("");
      setMemberEmail("");
      await refresh();
      setMessage("Household member added.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to add household member.");
    }
  };
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Resident portal</h1>
        {error && (
          <p className="message" role="alert">
            {error}
          </p>
        )}
        {summary && (
          <>
            <p>
              Welcome, {summary.resident.fullName}. Submit a service request and follow its progress
              here.
            </p>
            {message && <p className="message">{message}</p>}
            <h2>Your lease</h2>
            {summary.leases.length ? (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Term</th>
                      <th>Rent</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.leases.map((lease) => (
                      <tr key={lease.id}>
                        <td>
                          {lease.startsOn} – {lease.endsOn}
                        </td>
                        <td>${lease.monthlyRent.toLocaleString()}</td>
                        <td>{lease.status}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p>No lease records are available.</p>
            )}
            <h2>Household members</h2>
            {summary.householdMembers.length ? (
              <ul>
                {summary.householdMembers.map((member) => (
                  <li key={member.id}>
                    {member.fullName} — {member.relationship}
                    {member.email ? ` — ${member.email}` : ""}
                  </li>
                ))}
              </ul>
            ) : (
              <p>No household members yet.</p>
            )}
            <h2>Your service requests</h2>
            {summary.requests.length ? (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Request</th>
                      <th>Status</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.requests.map((request) => (
                      <tr key={request.id}>
                        <td>{request.title}</td>
                        <td>{request.status}</td>
                        <td>{request.residentVisibleNotes ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p>No service requests yet.</p>
            )}
            <h2>Payments</h2>
            {payments.length ? (
              <ul>
                {payments.map((payment) => (
                  <li key={payment.id}>
                    ${payment.amount.toLocaleString()} due {payment.dueOn} — {payment.status}
                    {payment.chargeId ? " — linked to lease charge" : ""}
                    {payment.reference ? ` — ${payment.reference}` : ""}
                  </li>
                ))}
              </ul>
            ) : (
              <p>No payments submitted yet.</p>
            )}
            <h2>Lease charges</h2>
            {charges.length ? (
              <ul>
                {charges.map((charge) => (
                  <li key={charge.id}>
                    {charge.description} — ${charge.amount.toLocaleString()} due {charge.dueOn} —{" "}
                    {charge.status}
                  </li>
                ))}
              </ul>
            ) : (
              <p>No lease charges yet.</p>
            )}
            <h2>Your documents</h2>
            {documents.length ? (
              <ul>
                {documents.map((document) => (
                  <li key={document.id}>
                    <a href={document.downloadUrl}>{document.fileName}</a>
                  </li>
                ))}
              </ul>
            ) : (
              <p>No resident-visible documents yet.</p>
            )}
            <h2>Lease documents</h2>
            {leaseDocuments.length ? (
              <ul>
                {leaseDocuments.map((document) => (
                  <li key={document.id}>
                    <a href={document.documentUrl} target="_blank" rel="noreferrer">
                      {document.title}
                    </a>{" "}
                    — {document.status}
                  </li>
                ))}
              </ul>
            ) : (
              <p>No lease documents yet.</p>
            )}
            <h2>Announcements</h2>
            {announcements.length ? (
              <div>
                {announcements.map((announcement) => (
                  <article key={announcement.id}>
                    <h3>{announcement.title}</h3>
                    <p>{announcement.body}</p>
                  </article>
                ))}
              </div>
            ) : (
              <p>No announcements yet.</p>
            )}
          </>
        )}
      </section>
      {summary?.occupancy && (
        <section className="panel">
          <h2>Submit a service request</h2>
          <form className="form-grid" onSubmit={(event) => void submit(event)}>
            <input
              aria-label="Service request title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              placeholder="What needs attention?"
              required
            />
            <textarea
              aria-label="Service request details"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              placeholder="Add details"
              required
            />
            <button type="submit">Submit request</button>
          </form>
        </section>
      )}
      {summary?.leases.length ? (
        <section className="panel">
          <h2>Submit a payment</h2>
          <form className="form-grid" onSubmit={(event) => void submitPayment(event)}>
            <select
              aria-label="Payment lease charge"
              value={paymentChargeId}
              onChange={(event) => {
                const chargeId = event.target.value;
                setPaymentChargeId(chargeId);
                const charge = charges.find((item) => item.id === chargeId);
                if (charge) {
                  setPaymentAmount(String(charge.amount));
                  setPaymentDueOn(charge.dueOn);
                }
              }}
            >
              <option value="">Unlinked payment</option>
              {charges
                .filter((charge) => charge.status === "Open")
                .map((charge) => (
                  <option key={charge.id} value={charge.id}>
                    {charge.description} — ${charge.amount.toLocaleString()} due {charge.dueOn}
                  </option>
                ))}
            </select>
            <input
              aria-label="Payment amount"
              type="number"
              min="0.01"
              step="0.01"
              value={paymentAmount}
              onChange={(event) => setPaymentAmount(event.target.value)}
              placeholder="Amount"
              required
            />
            <input
              aria-label="Payment due date"
              type="date"
              value={paymentDueOn}
              onChange={(event) => setPaymentDueOn(event.target.value)}
              required
            />
            <input
              aria-label="Payment reference"
              value={paymentReference}
              onChange={(event) => setPaymentReference(event.target.value)}
              placeholder="Reference (optional)"
            />
            <button type="submit">Submit payment</button>
          </form>
        </section>
      ) : null}
      {summary && (
        <section className="detail-grid">
          <form className="panel form-grid" onSubmit={(event) => void saveProfile(event)}>
            <h2>Your profile</h2>
            <input
              aria-label="Profile full name"
              value={fullName}
              onChange={(event) => setFullName(event.target.value)}
              required
            />
            <input
              aria-label="Profile email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <input
              aria-label="Profile phone"
              value={phone}
              onChange={(event) => setPhone(event.target.value)}
            />
            <button type="submit">Save profile</button>
          </form>
          <form className="panel form-grid" onSubmit={(event) => void savePreferences(event)}>
            <h2>Communication preferences</h2>
            <label>
              <input
                type="checkbox"
                checked={emailEnabled}
                onChange={(event) => setEmailEnabled(event.target.checked)}
              />{" "}
              Email notifications
            </label>
            <label>
              <input
                type="checkbox"
                checked={smsEnabled}
                onChange={(event) => setSmsEnabled(event.target.checked)}
              />{" "}
              SMS notifications
            </label>
            <button type="submit">Save preferences</button>
          </form>
          <form className="panel form-grid" onSubmit={(event) => void addHouseholdMember(event)}>
            <h2>Add household member</h2>
            <input
              aria-label="Household member name"
              value={memberName}
              onChange={(event) => setMemberName(event.target.value)}
              required
            />
            <input
              aria-label="Household member relationship"
              value={memberRelationship}
              onChange={(event) => setMemberRelationship(event.target.value)}
              placeholder="Relationship"
              required
            />
            <input
              aria-label="Household member email"
              type="email"
              value={memberEmail}
              onChange={(event) => setMemberEmail(event.target.value)}
            />
            <button type="submit">Add household member</button>
          </form>
        </section>
      )}
    </AppShell>
  );
}
