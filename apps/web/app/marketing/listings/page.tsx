"use client";
import { useEffect, useMemo, useState } from "react";
import { AppShell } from "../../components/app-shell";
import { ProtectedPage } from "../../components/protected-page";
import {
  api,
  type Inquiry,
  type Applicant,
  type Listing,
  type PropertyReference,
  type Session,
  type Showing,
} from "../../../lib/api";
import { hasCapability } from "../../../lib/capabilities";

export default function ListingsPage() {
  return (
    <ProtectedPage capability="Work.Read">
      {(session) => <ListingsContent session={session} />}
    </ProtectedPage>
  );
}

function ListingsContent({ session }: { session: Session }) {
  const [properties, setProperties] = useState<PropertyReference[]>([]);
  const [listings, setListings] = useState<Listing[]>([]);
  const [propertyId, setPropertyId] = useState("");
  const [headline, setHeadline] = useState("");
  const [availableOn, setAvailableOn] = useState("");
  const [monthlyRent, setMonthlyRent] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [selectedListing, setSelectedListing] = useState<Listing | null>(null);
  const [inquiries, setInquiries] = useState<Inquiry[]>([]);
  const [showings, setShowings] = useState<Showing[]>([]);
  const [applicants, setApplicants] = useState<Applicant[]>([]);
  const [prospectName, setProspectName] = useState("");
  const [showingProspectName, setShowingProspectName] = useState("");
  const [prospectEmail, setProspectEmail] = useState("");
  const [inquiryMessage, setInquiryMessage] = useState("");
  const [leadSource, setLeadSource] = useState("");
  const [showingDate, setShowingDate] = useState("");
  const canManage = hasCapability(session, "Leasing.Manage");
  const refresh = () =>
    Promise.all([api.properties.list(), api.marketing.listings.list()])
      .then(([propertyList, listingList]) => {
        setProperties(propertyList);
        setListings(listingList);
      })
      .catch(() => setError("Unable to load listings."));
  useEffect(() => {
    void refresh();
  }, []);
  const propertyNames = useMemo(
    () => new Map(properties.map((property) => [property.id, property.name])),
    [properties],
  );
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError("");
    setMessage("");
    try {
      await api.marketing.listings.create({
        propertyId,
        headline,
        availableOn: availableOn || null,
        monthlyRent: monthlyRent ? Number(monthlyRent) : null,
      });
      setHeadline("");
      setAvailableOn("");
      setMonthlyRent("");
      await refresh();
      setMessage("Listing created.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to create listing.");
    }
  };
  const changeStatus = async (listing: Listing) => {
    try {
      await (listing.status === "Published"
        ? api.marketing.listings.unpublish(listing.id)
        : api.marketing.listings.publish(listing.id));
      await refresh();
      setMessage(listing.status === "Published" ? "Listing unpublished." : "Listing published.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to update listing.");
    }
  };
  const loadActivity = async (listing: Listing) => {
    setSelectedListing(listing);
    setError("");
    try {
      const [inquiryList, showingList, applicantList] = await Promise.all([
        api.marketing.listings.inquiries(listing.id),
        api.marketing.listings.showings(listing.id),
        api.marketing.listings.applicants(listing.id),
      ]);
      setInquiries(inquiryList);
      setShowings(showingList);
      setApplicants(applicantList);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load listing activity.");
    }
  };
  const createInquiry = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!selectedListing) return;
    try {
      await api.marketing.listings.createInquiry(selectedListing.id, {
        prospectName,
        email: prospectEmail,
        message: inquiryMessage || null,
        leadSource: leadSource || null,
      });
      setProspectName("");
      setProspectEmail("");
      setInquiryMessage("");
      setLeadSource("");
      await loadActivity(selectedListing);
      setMessage("Inquiry recorded.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to record inquiry.");
    }
  };
  const createShowing = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!selectedListing) return;
    try {
      await api.marketing.listings.createShowing(selectedListing.id, {
        prospectName: showingProspectName,
        scheduledAt: new Date(showingDate).toISOString(),
      });
      setShowingProspectName("");
      setShowingDate("");
      await loadActivity(selectedListing);
      setMessage("Showing requested.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to request showing.");
    }
  };
  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Listings</h1>
        <p>Publish availability, capture leasing interest, and manage showing readiness.</p>
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
                <th>Property</th>
                <th>Headline</th>
                <th>Available</th>
                <th>Rent</th>
                <th>Status</th>
                {canManage && <th>Action</th>}
              </tr>
            </thead>
            <tbody>
              {listings.map((listing) => (
                <tr key={listing.id}>
                  <td>{propertyNames.get(listing.propertyId) ?? "—"}</td>
                  <td>{listing.headline}</td>
                  <td>{listing.availableOn ?? "—"}</td>
                  <td>
                    {listing.monthlyRent == null ? "—" : `$${listing.monthlyRent.toLocaleString()}`}
                  </td>
                  <td>{listing.status}</td>
                  {canManage && (
                    <td>
                      <button type="button" onClick={() => void changeStatus(listing)}>
                        {listing.status === "Published" ? "Unpublish" : "Publish"}
                      </button>
                      <button type="button" onClick={() => void loadActivity(listing)}>
                        Activity
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
          {!listings.length && <p>No listings yet.</p>}
        </div>
      </section>
      {canManage && (
        <section className="panel">
          <h2>Create listing</h2>
          <form className="form-grid" onSubmit={(event) => void submit(event)}>
            <select
              aria-label="Listing property"
              value={propertyId}
              onChange={(event) => setPropertyId(event.target.value)}
              required
            >
              <option value="">Select property</option>
              {properties.map((property) => (
                <option key={property.id} value={property.id}>
                  {property.name}
                </option>
              ))}
            </select>
            <input
              aria-label="Listing headline"
              value={headline}
              onChange={(event) => setHeadline(event.target.value)}
              placeholder="Headline"
              required
            />
            <input
              aria-label="Available date"
              type="date"
              value={availableOn}
              onChange={(event) => setAvailableOn(event.target.value)}
            />
            <input
              aria-label="Monthly rent"
              type="number"
              min="0"
              value={monthlyRent}
              onChange={(event) => setMonthlyRent(event.target.value)}
              placeholder="Monthly rent"
            />
            <button type="submit">Create listing</button>
          </form>
        </section>
      )}
      {selectedListing && (
        <section className="panel">
          <h2>Activity: {selectedListing.headline}</h2>
          {selectedListing.status !== "Published" && (
            <p>Publish this listing before recording new inquiries or showing requests.</p>
          )}
          {selectedListing.status === "Published" && canManage && (
            <div className="form-grid">
              <form onSubmit={(event) => void createInquiry(event)}>
                <h3>Record inquiry</h3>
                <input
                  aria-label="Prospect name"
                  value={prospectName}
                  onChange={(event) => setProspectName(event.target.value)}
                  placeholder="Prospect name"
                  required
                />
                <input
                  aria-label="Prospect email"
                  type="email"
                  value={prospectEmail}
                  onChange={(event) => setProspectEmail(event.target.value)}
                  placeholder="Email"
                  required
                />
                <textarea
                  aria-label="Inquiry message"
                  value={inquiryMessage}
                  onChange={(event) => setInquiryMessage(event.target.value)}
                  placeholder="Message"
                />
                <input
                  aria-label="Inquiry lead source"
                  value={leadSource}
                  onChange={(event) => setLeadSource(event.target.value)}
                  placeholder="Lead source (e.g. Website)"
                />
                <button type="submit">Record inquiry</button>
              </form>
              <form onSubmit={(event) => void createShowing(event)}>
                <h3>Request showing</h3>
                <input
                  aria-label="Showing prospect"
                  value={showingProspectName}
                  onChange={(event) => setShowingProspectName(event.target.value)}
                  placeholder="Prospect name"
                  required
                />
                <input
                  aria-label="Showing date"
                  type="datetime-local"
                  value={showingDate}
                  onChange={(event) => setShowingDate(event.target.value)}
                  required
                />
                <button type="submit">Request showing</button>
              </form>
            </div>
          )}
          <h3>Inquiries</h3>
          {inquiries.map((inquiry) => (
            <p key={inquiry.id}>
              {inquiry.prospectName} ({inquiry.email}) — {inquiry.status} — source:{" "}
              {inquiry.leadSource ?? "Unknown"}
              {canManage && inquiry.status !== "Closed" && (
                <button
                  type="button"
                  onClick={() =>
                    void api.marketing.listings
                      .inquiryStatus(
                        selectedListing.id,
                        inquiry.id,
                        inquiry.status === "New" ? "Contacted" : "Closed",
                      )
                      .then(() => loadActivity(selectedListing))
                  }
                >
                  Mark {inquiry.status === "New" ? "contacted" : "closed"}
                </button>
              )}
              {canManage && !applicants.some((applicant) => applicant.inquiryId === inquiry.id) && (
                <button
                  type="button"
                  onClick={() =>
                    void api.marketing.listings
                      .createApplicant(selectedListing.id, inquiry.id)
                      .then(() => loadActivity(selectedListing))
                  }
                >
                  Convert to applicant
                </button>
              )}
            </p>
          ))}
          {!inquiries.length && <p>No inquiries yet.</p>}
          <h3>Applicants</h3>
          {applicants.map((applicant) => (
            <p key={applicant.id}>
              {applicant.prospectName} ({applicant.email}) — {applicant.status}
              {canManage && applicant.status === "New" && (
                <button
                  type="button"
                  onClick={() =>
                    void api.marketing.listings
                      .applicantStatus(selectedListing.id, applicant.id, "Screening")
                      .then(() => loadActivity(selectedListing))
                  }
                >
                  Start screening
                </button>
              )}
              {canManage && applicant.status === "Screening" && (
                <button
                  type="button"
                  onClick={() =>
                    void api.marketing.listings
                      .applicantStatus(selectedListing.id, applicant.id, "Approved")
                      .then(() => loadActivity(selectedListing))
                  }
                >
                  Approve
                </button>
              )}
            </p>
          ))}
          {!applicants.length && <p>No applicants yet.</p>}
          <h3>Showings</h3>
          {showings.map((showing) => (
            <p key={showing.id}>
              {showing.prospectName} — {new Date(showing.scheduledAt).toLocaleString()} —{" "}
              {showing.status}
              {canManage && showing.status === "Requested" && (
                <button
                  type="button"
                  onClick={() =>
                    void api.marketing.listings
                      .showingStatus(selectedListing.id, showing.id, "Confirmed")
                      .then(() => loadActivity(selectedListing))
                  }
                >
                  Confirm
                </button>
              )}
            </p>
          ))}
          {!showings.length && <p>No showings yet.</p>}
        </section>
      )}
    </AppShell>
  );
}
