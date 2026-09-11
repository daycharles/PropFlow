"use client";
import { useEffect, useMemo, useState } from "react";
import { AppShell } from "../components/app-shell";
import { ProtectedPage } from "../components/protected-page";
import { api, type Portfolio, type PropertyReference, type Session } from "../../lib/api";
import { hasCapability } from "../../lib/capabilities";

export default function PropertiesPage() {
  return (
    <ProtectedPage capability="Work.Read">
      {(session) => <PropertiesContent session={session} />}
    </ProtectedPage>
  );
}

function PropertiesContent({ session }: { session: Session }) {
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [properties, setProperties] = useState<PropertyReference[]>([]);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [portfolioName, setPortfolioName] = useState("");
  const [propertyName, setPropertyName] = useState("");
  const [propertyPortfolioId, setPropertyPortfolioId] = useState("");
  const [propertyTimeZone, setPropertyTimeZone] = useState("America/New_York");
  const [buildingPropertyId, setBuildingPropertyId] = useState("");
  const [buildingName, setBuildingName] = useState("");
  const [spacePropertyId, setSpacePropertyId] = useState("");
  const [spaceBuildingId, setSpaceBuildingId] = useState("");
  const [spaceCode, setSpaceCode] = useState("");
  const [selectedProperty, setSelectedProperty] = useState<PropertyReference | null>(null);
  const [propertyDetail, setPropertyDetail] = useState<Awaited<
    ReturnType<typeof api.properties.get>
  > | null>(null);
  const [editName, setEditName] = useState("");
  const [editTimeZone, setEditTimeZone] = useState("");
  const [contactName, setContactName] = useState("");
  const [contactRole, setContactRole] = useState("");
  const [contactEmail, setContactEmail] = useState("");
  const [contactPhone, setContactPhone] = useState("");
  const [documentTitle, setDocumentTitle] = useState("");
  const [documentUrl, setDocumentUrl] = useState("");
  const [documentType, setDocumentType] = useState("");
  const canManage = hasCapability(session, "Properties.Manage");

  const refresh = () =>
    Promise.all([api.portfolios.list(), api.properties.list()])
      .then(([portfolioList, propertyList]) => {
        setPortfolios(portfolioList);
        setProperties(propertyList);
      })
      .catch(() => setError("Unable to load the property portfolio."));

  useEffect(() => {
    void refresh();
  }, []);

  const runMutation = async (action: () => Promise<unknown>, success: string) => {
    setError("");
    setMessage("");
    try {
      await action();
      await refresh();
      setMessage(success);
    } catch (mutationError) {
      setError(mutationError instanceof Error ? mutationError.message : "Unable to save changes.");
    }
  };

  const openProperty = async (property: PropertyReference) => {
    setError("");
    try {
      setSelectedProperty(property);
      setEditName(property.name);
      setEditTimeZone(property.timeZoneId);
      setPropertyDetail(await api.properties.get(property.id));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Unable to load property details.");
    }
  };
  const renameBuilding = async (
    property: PropertyReference,
    buildingId: string,
    currentName: string,
  ) => {
    const name = window.prompt("Building name", currentName);
    if (!name) return;
    await runMutation(
      () => api.properties.buildings.rename(property.id, buildingId, { name }),
      "Building updated.",
    );
    await openProperty(property);
  };
  const updateSpace = async (property: PropertyReference, spaceId: string, currentCode: string) => {
    const code = window.prompt("Space code", currentCode);
    if (!code) return;
    await runMutation(
      () => api.properties.spaces.update(property.id, spaceId, { buildingId: null, code }),
      "Space updated.",
    );
    await openProperty(property);
  };

  const visible = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (!normalized) return properties;
    return properties.filter((property) => property.name.toLowerCase().includes(normalized));
  }, [properties, query]);

  return (
    <AppShell session={session}>
      <section className="panel">
        <h1>Properties</h1>
        <p>Portfolio and property hierarchy for your organization.</p>
        {canManage && (
          <div className="table-wrap">
            <h2>Portfolios</h2>
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {portfolios.map((portfolio) => (
                  <tr key={portfolio.id}>
                    <td>{portfolio.name}</td>
                    <td>
                      <button
                        type="button"
                        onClick={() => {
                          const name = window.prompt("Portfolio name", portfolio.name);
                          if (name)
                            void runMutation(
                              () => api.portfolios.rename(portfolio.id, { name }),
                              "Portfolio updated.",
                            );
                        }}
                      >
                        Rename
                      </button>{" "}
                      <button
                        type="button"
                        onClick={() =>
                          void runMutation(
                            () => api.portfolios.archive(portfolio.id),
                            "Portfolio archived.",
                          )
                        }
                      >
                        Archive
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <label>
          Search properties
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search by property name"
          />
        </label>
        {error ? (
          <p className="message" role="alert">
            {error}
          </p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Portfolio</th>
                  <th>Property</th>
                  <th>Time zone</th>
                  {canManage && <th>Action</th>}
                </tr>
              </thead>
              <tbody>
                {visible.map((property) => (
                  <tr key={property.id}>
                    <td>
                      {portfolios.find((portfolio) => portfolio.id === property.portfolioId)
                        ?.name ?? "—"}
                    </td>
                    <td>{property.name}</td>
                    <td>{property.timeZoneId}</td>
                    {canManage && (
                      <td>
                        <button type="button" onClick={() => void openProperty(property)}>
                          Open details
                        </button>{" "}
                        <button
                          type="button"
                          onClick={() =>
                            void runMutation(
                              () => api.properties.archive(property.id),
                              "Property archived.",
                            )
                          }
                        >
                          Archive
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
            {!visible.length && <p>No properties match this search.</p>}
          </div>
        )}
      </section>
      {canManage && (
        <section className="detail-grid">
          <div className="panel">
            <h2>Manage hierarchy</h2>
            <p>Create the portfolio, property, building, and space records used by operations.</p>
            {message && <p className="message">{message}</p>}
            {error && (
              <p className="message" role="alert">
                {error}
              </p>
            )}
          </div>
          <div className="panel form-grid">
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void runMutation(
                  () => api.portfolios.create({ name: portfolioName }),
                  "Portfolio created.",
                ).then(() => setPortfolioName(""));
              }}
            >
              <h3>Portfolio</h3>
              <input
                aria-label="New portfolio name"
                value={portfolioName}
                onChange={(event) => setPortfolioName(event.target.value)}
                placeholder="Portfolio name"
                required
              />
              <button type="submit">Create portfolio</button>
            </form>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void runMutation(
                  () =>
                    api.properties.create({
                      portfolioId: propertyPortfolioId,
                      name: propertyName,
                      timeZoneId: propertyTimeZone,
                    }),
                  "Property created.",
                ).then(() => setPropertyName(""));
              }}
            >
              <h3>Property</h3>
              <select
                aria-label="Property portfolio"
                value={propertyPortfolioId}
                onChange={(event) => setPropertyPortfolioId(event.target.value)}
                required
              >
                <option value="">Select portfolio</option>
                {portfolios.map((portfolio) => (
                  <option key={portfolio.id} value={portfolio.id}>
                    {portfolio.name}
                  </option>
                ))}
              </select>
              <input
                aria-label="New property name"
                value={propertyName}
                onChange={(event) => setPropertyName(event.target.value)}
                placeholder="Property name"
                required
              />
              <input
                aria-label="Property time zone"
                value={propertyTimeZone}
                onChange={(event) => setPropertyTimeZone(event.target.value)}
                placeholder="America/New_York"
                required
              />
              <button type="submit">Create property</button>
            </form>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void runMutation(
                  () => api.properties.buildings.create(buildingPropertyId, { name: buildingName }),
                  "Building created.",
                ).then(() => setBuildingName(""));
              }}
            >
              <h3>Building</h3>
              <select
                aria-label="Building property"
                value={buildingPropertyId}
                onChange={(event) => setBuildingPropertyId(event.target.value)}
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
                aria-label="New building name"
                value={buildingName}
                onChange={(event) => setBuildingName(event.target.value)}
                placeholder="Building name"
                required
              />
              <button type="submit">Create building</button>
            </form>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void runMutation(
                  () =>
                    api.properties.spaces.create(spacePropertyId, {
                      buildingId: spaceBuildingId || null,
                      code: spaceCode,
                    }),
                  "Space created.",
                ).then(() => setSpaceCode(""));
              }}
            >
              <h3>Space</h3>
              <select
                aria-label="Space property"
                value={spacePropertyId}
                onChange={(event) => setSpacePropertyId(event.target.value)}
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
                aria-label="Building ID"
                value={spaceBuildingId}
                onChange={(event) => setSpaceBuildingId(event.target.value)}
                placeholder="Optional building ID"
              />
              <input
                aria-label="New space code"
                value={spaceCode}
                onChange={(event) => setSpaceCode(event.target.value)}
                placeholder="Space code"
                required
              />
              <button type="submit">Create space</button>
            </form>
          </div>
        </section>
      )}
      {selectedProperty && propertyDetail && (
        <section className="panel">
          <h2>Property details: {selectedProperty.name}</h2>
          <form
            className="form-grid"
            onSubmit={(event) => {
              event.preventDefault();
              void runMutation(
                () =>
                  api.properties.update(selectedProperty.id, {
                    portfolioId: selectedProperty.portfolioId,
                    name: editName,
                    timeZoneId: editTimeZone,
                  }),
                "Property updated.",
              );
            }}
          >
            <input
              aria-label="Edit property name"
              value={editName}
              onChange={(event) => setEditName(event.target.value)}
              required
            />
            <input
              aria-label="Edit property time zone"
              value={editTimeZone}
              onChange={(event) => setEditTimeZone(event.target.value)}
              required
            />
            <button type="submit">Save property</button>
          </form>
          <h3>Buildings</h3>
          {propertyDetail.buildings.length ? (
            <ul>
              {propertyDetail.buildings.map((building) => (
                <li key={building.id}>
                  {building.name}{" "}
                  <button
                    type="button"
                    onClick={() =>
                      void renameBuilding(selectedProperty, building.id, building.name)
                    }
                  >
                    Rename
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p>No buildings yet.</p>
          )}
          <h3>Spaces</h3>
          {propertyDetail.spaces.length ? (
            <ul>
              {propertyDetail.spaces.map((space) => (
                <li key={space.id}>
                  {space.code} — {space.isOccupied ? "Occupied" : "Vacant"}{" "}
                  <button
                    type="button"
                    onClick={() => void updateSpace(selectedProperty, space.id, space.code)}
                  >
                    Edit code
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p>No spaces yet.</p>
          )}
          <h3>Contacts</h3>
          {propertyDetail.contacts.length ? (
            <ul>
              {propertyDetail.contacts.map((contact) => (
                <li key={contact.id}>
                  {contact.fullName} — {contact.role}
                  {contact.email ? ` — ${contact.email}` : ""}
                  {contact.phone ? ` — ${contact.phone}` : ""}
                </li>
              ))}
            </ul>
          ) : (
            <p>No contacts yet.</p>
          )}
          <form
            className="form-grid"
            onSubmit={(event) => {
              event.preventDefault();
              void runMutation(
                () =>
                  api.properties.contacts.create(selectedProperty.id, {
                    fullName: contactName,
                    role: contactRole,
                    email: contactEmail || null,
                    phone: contactPhone || null,
                  }),
                "Contact added.",
              ).then(() => {
                setContactName("");
                setContactRole("");
                setContactEmail("");
                setContactPhone("");
                return openProperty(selectedProperty);
              });
            }}
          >
            <input
              aria-label="Property contact name"
              value={contactName}
              onChange={(event) => setContactName(event.target.value)}
              placeholder="Contact name"
              required
            />
            <input
              aria-label="Property contact role"
              value={contactRole}
              onChange={(event) => setContactRole(event.target.value)}
              placeholder="Role"
              required
            />
            <input
              aria-label="Property contact email"
              type="email"
              value={contactEmail}
              onChange={(event) => setContactEmail(event.target.value)}
              placeholder="Email"
            />
            <input
              aria-label="Property contact phone"
              value={contactPhone}
              onChange={(event) => setContactPhone(event.target.value)}
              placeholder="Phone"
            />
            <button type="submit">Add contact</button>
          </form>
          <h3>Documents</h3>
          {propertyDetail.documents.length ? (
            <ul>
              {propertyDetail.documents.map((document) => (
                <li key={document.id}>
                  <a href={document.documentUrl} target="_blank" rel="noreferrer">
                    {document.title}
                  </a>
                  {document.documentType ? ` — ${document.documentType}` : ""}
                </li>
              ))}
            </ul>
          ) : (
            <p>No property documents yet.</p>
          )}
          <form
            className="form-grid"
            onSubmit={(event) => {
              event.preventDefault();
              void runMutation(
                () =>
                  api.properties.documents.create(selectedProperty.id, {
                    title: documentTitle,
                    documentUrl,
                    documentType: documentType || null,
                  }),
                "Document added.",
              ).then(() => {
                setDocumentTitle("");
                setDocumentUrl("");
                setDocumentType("");
                return openProperty(selectedProperty);
              });
            }}
          >
            <input
              aria-label="Property document title"
              value={documentTitle}
              onChange={(event) => setDocumentTitle(event.target.value)}
              placeholder="Document title"
              required
            />
            <input
              aria-label="Property document URL"
              type="url"
              value={documentUrl}
              onChange={(event) => setDocumentUrl(event.target.value)}
              placeholder="https://..."
              required
            />
            <input
              aria-label="Property document type"
              value={documentType}
              onChange={(event) => setDocumentType(event.target.value)}
              placeholder="Type (optional)"
            />
            <button type="submit">Add document</button>
          </form>
        </section>
      )}
    </AppShell>
  );
}
