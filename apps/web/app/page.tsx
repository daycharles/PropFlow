"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import {
  api,
  ApiError,
  type Category,
  type Employee,
  type MessageTemplate,
  type Session,
  type Vendor,
  type SavedView,
  type WorkItem,
  type WorkListQuery,
} from "../lib/api";
import { hasCapability } from "../lib/capabilities";
import { AppShell } from "./components/app-shell";

const statuses = [
  "Draft",
  "New",
  "Assigned",
  "Scheduled",
  "InProgress",
  "OnHold",
  "Completed",
  "Cancelled",
];
const priorities = ["Low", "Normal", "High", "Critical"];
type Sort = "title" | "status" | "priority" | "dueDate";

export default function Home() {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);
  const [message, setMessage] = useState("");
  const [submitting, setSubmitting] = useState(false);
  async function bootstrap() {
    try {
      setSession(await api.session());
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 401))
        setMessage("We could not load your session. Please try again.");
    } finally {
      setReady(true);
    }
  }
  useEffect(() => {
    const timer = window.setTimeout(() => void bootstrap(), 0);
    return () => window.clearTimeout(timer);
  }, []);
  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    setSubmitting(true);
    setMessage("");
    try {
      await api.auth.login({
        organizationSlug: String(values.get("organizationSlug") ?? ""),
        email: String(values.get("email") ?? ""),
        password: String(values.get("password") ?? ""),
      });
      await bootstrap();
    } catch (error) {
      setMessage(
        error instanceof ApiError && error.status === 429
          ? "Too many attempts. Please wait and try again."
          : "Unable to sign in with those credentials and organization.",
      );
    } finally {
      setSubmitting(false);
    }
  }
  if (!ready)
    return (
      <main className="centered">
        <p>Loading PropFlow…</p>
      </main>
    );
  if (!session)
    return (
      <main className="login">
        <h1>PropFlow</h1>
        <p>Internal property operations</p>
        <form onSubmit={login}>
          <label>
            Organization slug
            <input name="organizationSlug" autoComplete="organization" required />
          </label>
          <label>
            Email
            <input name="email" type="email" autoComplete="email" required />
          </label>
          <label>
            Password
            <input name="password" type="password" autoComplete="current-password" required />
          </label>
          <button disabled={submitting}>{submitting ? "Signing in…" : "Sign in"}</button>
        </form>
        {message && (
          <p className="message" role="alert">
            {message}
          </p>
        )}
      </main>
    );
  return (
    <AppShell session={session} onLogout={() => setSession(null)}>
      <WorkList session={session} />
    </AppShell>
  );
}

const defaultQuery: WorkListQuery = { sort: "title", page: 1, pageSize: 100 };

function WorkList({ session }: { session: Session }) {
  const [work, setWork] = useState<WorkItem[]>([]);
  const [vendors, setVendors] = useState<Vendor[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [templates, setTemplates] = useState<MessageTemplate[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [query, setQuery] = useState<WorkListQuery>(defaultQuery);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [vendorId, setVendorId] = useState("");
  const [employeeId, setEmployeeId] = useState("");
  const [templateId, setTemplateId] = useState("");
  const [bulkAction, setBulkAction] = useState<"vendor" | "employee" | "message">("vendor");
  const [flow, setFlow] = useState<"choose" | "confirm" | "success" | null>(null);
  const [result, setResult] = useState<{
    changed: number;
    unchanged: number;
    total: number;
  } | null>(null);
  const [savedViews, setSavedViews] = useState<SavedView[]>([]);
  const [viewName, setViewName] = useState("");
  const [viewIsDefault, setViewIsDefault] = useState(false);
  // The default view is applied once, when reference data first arrives. Any user-driven query
  // change also sets this, so a late default view cannot clobber filters already on screen.
  const defaultViewApplied = useRef(false);
  async function saveView() {
    const name = viewName.trim();
    if (!name) return;
    try {
      const created = await api.savedViews.create({
        name,
        filters: query,
        isDefault: viewIsDefault,
      });
      // Saving a new default clears the previous one server-side, so re-read the list — through
      // loadReference so the read carries the same sequence guard as every other reference load.
      if (viewIsDefault) await loadReference();
      else setSavedViews((current) => [...current, created]);
      setViewName("");
      setViewIsDefault(false);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to save this view.");
    }
  }
  function applyView(view: SavedView) {
    defaultViewApplied.current = true;
    try {
      setQuery(JSON.parse(view.filters) as WorkListQuery);
    } catch {
      setError("This saved view has invalid filters.");
    }
  }
  async function deleteView(id: string) {
    try {
      await api.savedViews.delete(id);
      setSavedViews((current) => current.filter((view) => view.id !== id));
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : "Unable to delete this view.");
    }
  }
  const visibleSelected = useMemo(
    () => work.filter((item) => selected.has(item.id)),
    [work, selected],
  );
  // Every work request takes the next ticket; only the newest ticket may touch state. Without
  // this an earlier, unfiltered response can resolve last and overwrite a filtered one, leaving
  // rows on screen that contradict the filters (and never correcting themselves).
  const workRequest = useRef(0);
  async function loadWork(next = query) {
    const ticket = ++workRequest.current;
    setLoading(true);
    setError("");
    try {
      const listed = await api.work.list(next);
      if (ticket !== workRequest.current) return;
      setWork(listed.items);
      setSelected(
        (current) =>
          new Set([...current].filter((id) => listed.items.some((item) => item.id === id))),
      );
    } catch (cause) {
      if (ticket !== workRequest.current) return;
      setError(
        cause instanceof ApiError
          ? cause.message
          : "Unable to load work. Please refresh and try again.",
      );
    } finally {
      // A superseded request must not clear the spinner a newer one is still showing.
      if (ticket === workRequest.current) setLoading(false);
    }
  }
  // Vendors, saved views and categories do not depend on `query`, so they load once per mount
  // (and on an explicit Refresh) rather than on every keystroke.
  const referenceRequest = useRef(0);
  async function loadReference() {
    const ticket = ++referenceRequest.current;
    try {
      const [vendorList, viewList, categoryList, employeeList, templateList] = await Promise.all([
        api.vendors.list(),
        api.savedViews.list(),
        api.categories.list(),
        hasCapability(session, "Work.AssignEmployee") ? api.employees.list() : Promise.resolve([]),
        hasCapability(session, "Communications.SendMessage")
          ? api.messageTemplates.available()
          : Promise.resolve([]),
      ]);
      if (ticket !== referenceRequest.current) return;
      setVendors(vendorList.filter((vendor) => vendor.isActive));
      setSavedViews(viewList);
      setCategories(categoryList.filter((category) => !category.isArchived));
      setEmployees(employeeList.filter((employee) => employee.isActive));
      setTemplates(templateList);
      if (!defaultViewApplied.current) {
        defaultViewApplied.current = true;
        const preferred = viewList.find((view) => view.isDefault);
        // Applying the default view changes `query`, which re-runs the work effect below.
        if (preferred) applyView(preferred);
      }
    } catch (cause) {
      if (ticket !== referenceRequest.current) return;
      setError(
        cause instanceof ApiError ? cause.message : "Unable to load vendors, views and categories.",
      );
    }
  }
  useEffect(() => {
    const timer = window.setTimeout(() => void loadReference(), 0);
    return () => window.clearTimeout(timer);
    // Reference data is query-independent; it loads once for this mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => {
    const timer = window.setTimeout(() => void loadWork(), 0);
    return () => window.clearTimeout(timer);
    // loadWork reads the query from this render; its identity is not part of the request trigger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);
  function updateQuery(changes: Partial<WorkListQuery>) {
    // The user has taken control of the filters; a default view arriving late must not win.
    defaultViewApplied.current = true;
    setQuery((current) => ({ ...current, ...changes, page: 1 }));
  }
  function clearFilters() {
    defaultViewApplied.current = true;
    setQuery({ ...defaultQuery });
  }
  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }
  function sortBy(sort: Sort) {
    defaultViewApplied.current = true;
    setQuery((current) => ({
      ...current,
      sort,
      descending: current.sort === sort ? !current.descending : false,
    }));
  }
  async function runBulkAction() {
    if (
      (bulkAction === "vendor" && !vendorId) ||
      (bulkAction === "employee" && !employeeId) ||
      (bulkAction === "message" && !templateId)
    )
      return;
    setError("");
    try {
      const concurrencyTokens = Object.fromEntries(
        visibleSelected
          .filter((item) => item.rowVersion)
          .map((item) => [item.id, item.rowVersion!]),
      );
      const input = { workIds: visibleSelected.map((item) => item.id), concurrencyTokens };
      const response =
        bulkAction === "vendor"
          ? await api.work.bulkAssignVendor({ ...input, vendorId })
          : bulkAction === "employee"
            ? await api.work.bulkAssignEmployee({ ...input, employeeId })
            : await api.work.bulkSendResidentMessage({ workIds: input.workIds, templateId });
      setResult(response);
      setFlow("success");
      await loadWork();
    } catch (cause) {
      setFlow(null);
      // Refresh first: loadWork clears the error banner on entry, so setting the message before
      // it would wipe the message the user needs to read.
      await loadWork();
      setError(
        bulkAction !== "message" && cause instanceof ApiError && cause.status === 409
          ? "Some selected work changed. The list was refreshed; review it and try again."
          : cause instanceof ApiError
            ? cause.message
            : `Bulk ${bulkAction === "message" ? "message" : "assignment"} could not be completed.`,
      );
    }
  }
  const allSelected = work.length > 0 && work.every((item) => selected.has(item.id));
  const chosenVendor = vendors.find((vendor) => vendor.id === vendorId);
  const chosenEmployee = employees.find((employee) => employee.id === employeeId);
  const chosenTemplate = templates.find((template) => template.id === templateId);
  const actionLabel =
    bulkAction === "vendor"
      ? "Assign vendor"
      : bulkAction === "employee"
        ? "Assign employee"
        : "Send resident message";
  const defaultView = savedViews.find((view) => view.isDefault);
  const assignedTotal = result?.total ?? visibleSelected.length;
  if (!hasCapability(session, "Work.Read"))
    return (
      <section className="panel">
        <h1>Work</h1>
        <p>You do not have access to work items.</p>
      </section>
    );
  return (
    <section className="panel work-panel">
      <div className="work-heading">
        <div>
          <h1>Work</h1>
          <p>Find, prioritize, and assign operational work.</p>
        </div>
        <button
          className="secondary"
          onClick={() => {
            void loadWork();
            void loadReference();
          }}
          disabled={loading}
        >
          Refresh
        </button>
      </div>
      {error && (
        <p className="message" role="alert">
          {error}
        </p>
      )}
      <div className="filters" aria-label="Work filters">
        <label>
          Search
          <input
            value={query.search ?? ""}
            onChange={(event) => updateQuery({ search: event.target.value })}
            placeholder="Title, resident, or unit"
          />
        </label>
        <label>
          Status
          <select
            value={query.status ?? ""}
            onChange={(event) => updateQuery({ status: event.target.value || undefined })}
          >
            <option value="">All statuses</option>
            {statuses.map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
        </label>
        <label>
          Priority
          <select
            value={query.priority ?? ""}
            onChange={(event) => updateQuery({ priority: event.target.value || undefined })}
          >
            <option value="">All priorities</option>
            {priorities.map((value) => (
              <option key={value}>{value}</option>
            ))}
          </select>
        </label>
        <label>
          Category
          <select
            value={query.categoryId ?? ""}
            onChange={(event) => updateQuery({ categoryId: event.target.value || undefined })}
          >
            <option value="">All categories</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </label>
        <button className="secondary filter-clear" onClick={clearFilters}>
          Clear filters
        </button>
      </div>
      <div className="saved-views" aria-label="Saved views">
        <strong>Saved views</strong>
        <select
          aria-label="Apply saved view"
          defaultValue=""
          onChange={(event) => {
            const view = savedViews.find((item) => item.id === event.target.value);
            if (view) applyView(view);
            event.currentTarget.value = "";
          }}
        >
          <option value="">Apply a view…</option>
          {savedViews.map((view) => (
            <option key={view.id} value={view.id}>
              {viewLabel(view)}
            </option>
          ))}
        </select>
        <input
          aria-label="Saved view name"
          value={viewName}
          onChange={(event) => setViewName(event.target.value)}
          placeholder="View name"
        />
        <label className="inline-check">
          <input
            type="checkbox"
            checked={viewIsDefault}
            onChange={(event) => setViewIsDefault(event.target.checked)}
          />
          Make this my default view
        </label>
        <button className="secondary" onClick={() => void saveView()} disabled={!viewName.trim()}>
          Save current view
        </button>
        {savedViews.length > 0 && (
          <select
            aria-label="Delete saved view"
            defaultValue=""
            onChange={(event) => {
              if (event.target.value) void deleteView(event.target.value);
              event.currentTarget.value = "";
            }}
          >
            <option value="">Delete a view…</option>
            {savedViews.map((view) => (
              <option key={view.id} value={view.id}>
                {viewLabel(view)}
              </option>
            ))}
          </select>
        )}
        <small className="saved-views-note">
          {defaultView
            ? `Default view: ${defaultView.name} (applied when the list opens)`
            : "No default view yet."}
        </small>
      </div>
      {selected.size > 0 && (
        <div className="bulk-toolbar" role="status">
          <strong>{selected.size} selected</strong>
          <button
            onClick={() => {
              setBulkAction("vendor");
              setVendorId("");
              setFlow("choose");
            }}
            disabled={!hasCapability(session, "Work.AssignVendor")}
          >
            Assign vendor
          </button>
          <button
            onClick={() => {
              setBulkAction("employee");
              setEmployeeId("");
              setFlow("choose");
            }}
            disabled={!hasCapability(session, "Work.AssignEmployee")}
          >
            Assign employee
          </button>
          <button
            onClick={() => {
              setBulkAction("message");
              setTemplateId("");
              setFlow("choose");
            }}
            disabled={!hasCapability(session, "Communications.SendMessage")}
          >
            Send resident message
          </button>
          <button className="secondary" onClick={() => setSelected(new Set())}>
            Clear selection
          </button>
        </div>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>
                <input
                  aria-label="Select all visible work"
                  type="checkbox"
                  checked={allSelected}
                  onChange={() =>
                    setSelected(allSelected ? new Set() : new Set(work.map((item) => item.id)))
                  }
                />
              </th>
              <SortHeader
                label="Title"
                sort="title"
                active={query.sort}
                descending={query.descending}
                onSort={sortBy}
              />
              <SortHeader
                label="Status"
                sort="status"
                active={query.sort}
                descending={query.descending}
                onSort={sortBy}
              />
              <SortHeader
                label="Priority"
                sort="priority"
                active={query.sort}
                descending={query.descending}
                onSort={sortBy}
              />
              <th>Vendor</th>
              <SortHeader
                label="Due"
                sort="dueDate"
                active={query.sort}
                descending={query.descending}
                onSort={sortBy}
              />
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={7}>Loading work…</td>
              </tr>
            ) : work.length === 0 ? (
              <tr>
                <td colSpan={7}>No work matches these filters.</td>
              </tr>
            ) : (
              work.map((item) => (
                <tr key={item.id}>
                  <td>
                    <input
                      aria-label={`Select ${item.title}`}
                      type="checkbox"
                      checked={selected.has(item.id)}
                      onChange={() => toggle(item.id)}
                    />
                  </td>
                  <td>
                    <Link href={`/work/${item.id}`}>
                      <strong>{item.title}</strong>
                    </Link>
                    {item.propertyName && <small>{item.propertyName}</small>}
                  </td>
                  <td>
                    <span className={`badge ${statusClass(item.status)}`}>{item.status}</span>
                  </td>
                  <td>
                    <span className={`priority ${priorityClass(item.priority, item.status)} `}>
                      {item.priority}
                    </span>
                  </td>
                  <td>{item.vendorName ?? "Unassigned"}</td>
                  <td>{formatDate(item.dueDate)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      {flow && (
        <div className="modal-backdrop" role="presentation">
          <section
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="assignment-title"
          >
            {flow === "choose" && (
              <>
                <h2 id="assignment-title">{actionLabel}</h2>
                <p>
                  {bulkAction === "message"
                    ? "Send a resident message for"
                    : "Apply this assignment to"}{" "}
                  {visibleSelected.length} selected work{" "}
                  {visibleSelected.length === 1 ? "item" : "items"}.
                </p>
                {bulkAction === "vendor" && (
                  <label>
                    Vendor
                    <select value={vendorId} onChange={(event) => setVendorId(event.target.value)}>
                      <option value="">Choose a vendor</option>
                      {vendors.map((vendor) => (
                        <option key={vendor.id} value={vendor.id}>
                          {vendor.name}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                {bulkAction === "employee" && (
                  <label>
                    Employee
                    <select
                      value={employeeId}
                      onChange={(event) => setEmployeeId(event.target.value)}
                    >
                      <option value="">Choose an employee</option>
                      {employees.map((employee) => (
                        <option key={employee.id} value={employee.id}>
                          {employee.displayName}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                {bulkAction === "message" && (
                  <label>
                    Message template
                    <select
                      value={templateId}
                      onChange={(event) => setTemplateId(event.target.value)}
                    >
                      <option value="">Choose a template</option>
                      {templates.map((template) => (
                        <option key={template.id} value={template.id}>
                          {template.name} ({template.channel})
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                <div className="modal-actions">
                  <button className="secondary" onClick={() => setFlow(null)}>
                    Cancel
                  </button>
                  <button
                    disabled={
                      bulkAction === "vendor"
                        ? !vendorId
                        : bulkAction === "employee"
                          ? !employeeId
                          : !templateId
                    }
                    onClick={() => setFlow("confirm")}
                  >
                    Continue
                  </button>
                </div>
              </>
            )}
            {flow === "confirm" && (
              <>
                <h2 id="assignment-title">Confirm {actionLabel.toLowerCase()}</h2>
                <p>
                  <strong>
                    {bulkAction === "vendor"
                      ? chosenVendor?.name
                      : bulkAction === "employee"
                        ? chosenEmployee?.displayName
                        : chosenTemplate?.name}
                  </strong>{" "}
                  {bulkAction === "message"
                    ? "will be rendered and queued for"
                    : "will be applied to"}{" "}
                  {visibleSelected.length} work {visibleSelected.length === 1 ? "item" : "items"}.
                  {bulkAction === "message"
                    ? " Every selected resident must be contactable through the template channel."
                    : " This updates assignment history for each item."}
                </p>
                <div className="modal-actions">
                  <button className="secondary" onClick={() => setFlow("choose")}>
                    Back
                  </button>
                  <button
                    aria-label={bulkAction === "message" ? "Confirm message" : "Confirm assignment"}
                    onClick={() => void runBulkAction()}
                  >
                    Confirm
                  </button>
                </div>
              </>
            )}
            {flow === "success" && (
              <>
                <h2 id="assignment-title">
                  {bulkAction === "message"
                    ? "Resident message queued"
                    : `${bulkAction === "vendor" ? "Vendor" : "Employee"} assigned`}
                </h2>
                <p>
                  {bulkAction === "message" ? "Queued" : "Assigned"}{" "}
                  {result?.changed ?? assignedTotal} of {assignedTotal}{" "}
                  {assignedTotal === 1 ? "work item" : "work items"}
                  {bulkAction === "vendor"
                    ? ` to ${chosenVendor?.name}`
                    : bulkAction === "employee"
                      ? ` to ${chosenEmployee?.displayName}`
                      : ""}
                  {result && result.unchanged > 0
                    ? ` (${result.unchanged} already had this ${bulkAction === "message" ? "message queued" : "assignment"})`
                    : ""}
                  . The list has been refreshed.
                </p>
                <div className="modal-actions">
                  <button
                    onClick={() => {
                      setSelected(new Set());
                      setFlow(null);
                    }}
                  >
                    Done
                  </button>
                </div>
              </>
            )}
          </section>
        </div>
      )}
    </section>
  );
}
function SortHeader({
  label,
  sort,
  active,
  descending,
  onSort,
}: {
  label: string;
  sort: Sort;
  active?: string;
  descending?: boolean;
  onSort: (sort: Sort) => void;
}) {
  return (
    <th>
      <button className="sort-button" onClick={() => onSort(sort)}>
        {label}
        {active === sort ? (descending ? " ↓" : " ↑") : ""}
      </button>
    </th>
  );
}
function viewLabel(view: SavedView) {
  return view.isDefault ? `${view.name} (default)` : view.name;
}
function formatDate(value?: string | null) {
  return value
    ? new Intl.DateTimeFormat(undefined, {
        month: "short",
        day: "numeric",
        year: "numeric",
      }).format(new Date(value))
    : "—";
}

function statusClass(status: string) {
  return status === "Completed" ? "badge-complete" : status === "New" ? "badge-urgent" : "";
}

function priorityClass(priority: string, status: string) {
  return status === "Completed" || status === "Cancelled"
    ? ""
    : priority === "High" || priority === "Critical"
      ? "priority-urgent"
      : "";
}
