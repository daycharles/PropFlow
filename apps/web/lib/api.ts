export type Session = {
  userId: string;
  organizationId: string;
  role: string;
  capabilities: string[];
};
export type WorkItem = {
  id: string;
  title: string;
  description?: string | null;
  status: string;
  priority: string;
  workType?: string | null;
  categoryId?: string | null;
  categoryName?: string | null;
  propertyId?: string | null;
  propertyName?: string | null;
  buildingId?: string | null;
  spaceId?: string | null;
  assetId?: string | null;
  vendorId?: string | null;
  vendorName?: string | null;
  employeeId?: string | null;
  dueDate?: string | null;
  createdAt?: string | null;
  version?: number;
  rowVersion?: string | null;
};
export type WorkDetail = WorkItem & {
  residentId?: string | null;
  scheduledStart?: string | null;
  scheduledEnd?: string | null;
  completedAt?: string | null;
  cost?: number | null;
  internalNotes?: string | null;
  residentVisibleNotes?: string | null;
  version: number;
};
export type Attachment = {
  id: string;
  fileName: string;
  contentType: string;
  length: number;
  residentVisible: boolean;
  createdAt: string;
  retainUntil?: string | null;
};
export type TimelineEntry = {
  id: string;
  eventType?: string | null;
  occurredAt: string;
  actorId?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  relatedObjectType?: string | null;
  residentVisible?: boolean;
  communicationStatus?: "Queued" | "Sending" | "Sent" | "Failed" | null;
};
export type UpdateWorkInput = {
  title: string;
  description?: string | null;
  categoryId?: string | null;
  priority: string;
  propertyId: string;
  buildingId?: string | null;
  spaceId?: string | null;
  residentId?: string | null;
  assetId?: string | null;
  dueDate?: string | null;
  cost?: number | null;
  internalNotes?: string | null;
  residentVisibleNotes?: string | null;
  status?: string | null;
  scheduledStart?: string | null;
  scheduledEnd?: string | null;
  version: number;
};
export type Vendor = {
  id: string;
  name: string;
  email?: string | null;
  phone?: string | null;
  isActive: boolean;
};
export type Employee = {
  id: string;
  displayName: string;
  email?: string | null;
  phone?: string | null;
  isActive: boolean;
};
export type Portfolio = {
  id: string;
  name: string;
};
export type PropertyReference = {
  id: string;
  portfolioId: string;
  name: string;
  timeZoneId: string;
  isArchived?: boolean;
};
export type ResidentReference = {
  id: string;
  fullName: string;
  email?: string | null;
  phone?: string | null;
};
export type PropertyDetail = {
  property: PropertyReference;
  buildings: { id: string; name: string; isArchived?: boolean }[];
  spaces: {
    id: string;
    buildingId?: string | null;
    code: string;
    isArchived?: boolean;
    isOccupied?: boolean;
  }[];
  contacts: {
    id: string;
    propertyId: string;
    fullName: string;
    role: string;
    email?: string | null;
    phone?: string | null;
  }[];
  documents: {
    id: string;
    propertyId: string;
    title: string;
    documentUrl: string;
    documentType?: string | null;
    createdAt: string;
  }[];
  amenities: { id: string; propertyId: string; name: string; details?: string | null }[];
};
export type Lease = {
  id: string;
  residentId: string;
  spaceId: string;
  startsOn: string;
  endsOn: string;
  monthlyRent: number;
  securityDeposit?: number | null;
  status: "Draft" | "Active" | "Renewed" | "NoticeGiven" | "Ended";
  noticeDate?: string | null;
  moveOutOn?: string | null;
};
export type LeaseNotice = {
  id: string;
  leaseId: string;
  type: "Renewal" | "MoveOut";
  dueOn: string;
  notes?: string | null;
  status: "Open" | "Completed" | "Cancelled";
};
export type LeaseParty = {
  id: string;
  leaseId: string;
  fullName: string;
  role: string;
  email?: string | null;
};
export type ResidentPayment = {
  id: string;
  leaseId: string;
  residentId: string;
  chargeId?: string | null;
  amount: number;
  dueOn: string;
  reference?: string | null;
  status: "Submitted" | "Settled" | "Failed";
  submittedAt: string;
  settledAt?: string | null;
};
export type LeaseCharge = {
  id: string;
  leaseId: string;
  type: "Recurring" | "OneTime";
  description: string;
  amount: number;
  dueOn: string;
  status: "Open" | "Paid" | "Voided";
  createdAt: string;
};
export type Listing = {
  id: string;
  propertyId: string;
  spaceId?: string | null;
  headline: string;
  description?: string | null;
  availableOn?: string | null;
  monthlyRent?: number | null;
  status: "Draft" | "Published" | "Archived";
};
export type Inquiry = {
  id: string;
  listingId: string;
  prospectName: string;
  email: string;
  phone?: string | null;
  message?: string | null;
  leadSource?: string | null;
  status: "New" | "Contacted" | "Closed";
  createdAt: string;
};
export type Applicant = {
  id: string;
  listingId: string;
  inquiryId: string;
  prospectName: string;
  email: string;
  status: "New" | "Screening" | "Approved" | "Declined";
  createdAt: string;
};
export type Showing = {
  id: string;
  listingId: string;
  prospectName: string;
  scheduledAt: string;
  status: "Requested" | "Confirmed" | "Completed" | "Cancelled";
};
export type PortalSummary = {
  resident: ResidentReference & { smsConsent: string; emailConsent: string };
  occupancy?: { id: string; spaceId: string; movedInOn: string } | null;
  leases: Lease[];
  householdMembers: { id: string; fullName: string; relationship: string; email?: string | null }[];
  requests: {
    id: string;
    title: string;
    description?: string | null;
    status: string;
    priority: string;
    residentVisibleNotes?: string | null;
    createdAt: string;
  }[];
};
export type PortalDocument = {
  id: string;
  workId: string;
  fileName: string;
  contentType: string;
  length: number;
  createdAt: string;
  downloadUrl: string;
};
export type PortalLeaseDocument = {
  id: string;
  leaseId: string;
  title: string;
  documentUrl: string;
  status: "Draft" | "Sent" | "Signed" | "Expired";
  createdAt: string;
  signedAt?: string | null;
  signedBy?: string | null;
};
export type PortalAnnouncement = {
  id: string;
  title: string;
  body: string;
  createdAt: string;
  expiresAt?: string | null;
};
export type Announcement = PortalAnnouncement & { status: "Draft" | "Published" | "Archived" };
export type Asset = {
  id: string;
  propertyId: string;
  spaceId?: string | null;
  kind: string;
  name: string;
  manufacturer?: string | null;
  model?: string | null;
  serialNumber?: string | null;
  installedOn?: string | null;
  warrantyExpiresOn?: string | null;
  expectedServiceLifeYears?: number | null;
  condition: string;
  replacementCostEstimate?: number | null;
  notes?: string | null;
};
export type AssetHistoryItem = {
  id: string;
  title: string;
  status: string;
  priority: string;
  categoryName?: string | null;
  vendorName?: string | null;
  createdAt: string;
  completedAt?: string | null;
  cost?: number | null;
};
export type AssetHistory = {
  asset: Asset;
  ageInYears?: number | null;
  underWarranty: boolean;
  workOrderCount: number;
  totalCost: number;
  history: AssetHistoryItem[];
};
export type RepeatRepairAssessment = {
  repairThreshold: number;
  windowDays: number;
  matchByCategory: boolean;
  repairCount: number;
  since: string;
  totalCostInWindow: number;
  ageInYears?: number | null;
  isRepeatRepair: boolean;
};
export type AttentionSeverity = "Critical" | "Warning" | "Informational";
export type AttentionReason =
  | "UnassignedEmergency"
  | "SlaBreach"
  | "Overdue"
  | "WaitingOnVendor"
  | "WaitingOnResident"
  | "RepeatRepair"
  | "UnitTurnAtRisk";
export type AttentionFinding = {
  reason: AttentionReason;
  severity: AttentionSeverity;
  detail: string;
};
export type AttentionItem = {
  workId: string;
  title: string;
  propertyId: string;
  propertyName?: string | null;
  status: string;
  priority: string;
  dueDate?: string | null;
  // The most urgent severity among findings; findings carry every rule this work item tripped.
  severity: AttentionSeverity;
  findings: AttentionFinding[];
};
export type AttentionQueue = {
  items: AttentionItem[];
  criticalCount: number;
  warningCount: number;
  informationalCount: number;
};
export type IntegrationSource = { sourceSystem: string; displayName: string };
export type IntegrationHealth = {
  id: string;
  sourceSystem: string;
  displayName: string;
  isEnabled: boolean;
  lastAttemptedAt?: string | null;
  lastSucceededAt?: string | null;
  consecutiveFailures: number;
  lastError?: string | null;
  trackedRecords: number;
  failedRecords: number;
};
export type IntegrationSyncState = "Pending" | "Synced" | "Failed";
export type IntegrationRecord = {
  id: string;
  connectionId: string;
  kind: string;
  externalId: string;
  syncState: IntegrationSyncState;
  lastSeenAt?: string | null;
  lastError?: string | null;
  contentHash?: string | null;
};
export type IntegrationRecordsPage = {
  items: IntegrationRecord[];
  totalCount: number;
  page: number;
  pageSize: number;
};
export type SyncReport = {
  outcome: string;
  seen: number;
  added: number;
  updated: number;
  failed: number;
  error?: string | null;
};
export type SearchHitType =
  | "Property"
  | "Building"
  | "Space"
  | "Resident"
  | "Vendor"
  | "Employee"
  | "Category"
  | "Asset"
  | "Work";
export type SearchHit = {
  type: SearchHitType;
  id: string;
  label: string;
  sublabel?: string | null;
  score: number;
};
export type WorkListQuery = {
  search?: string;
  status?: string;
  priority?: string;
  categoryId?: string;
  propertyId?: string;
  spaceId?: string;
  sort?: string;
  descending?: boolean;
  page?: number;
  pageSize?: number;
};
export type WorkListResult = { items: WorkItem[]; totalCount: number };
export type BulkAssignmentResult = { changed: number; unchanged: number; total: number };
export type MessageTemplate = {
  id: string;
  name: string;
  channel: "Sms" | "Email";
  subject?: string | null;
  body: string;
  isActive: boolean;
};
export type Category = { id: string; name: string; isArchived: boolean; sortOrder: number };
export type AutomationRule = {
  id: string;
  name: string;
  trigger: string;
  conditions: unknown[];
  actions: unknown[];
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
};
export type SavedView = {
  id: string;
  name: string;
  filters: string;
  columns: string;
  isDefault: boolean;
};
export type Invitation = {
  id: string;
  email: string;
  role: string;
  createdAt: string;
  expiresAt: string;
};
// The token is returned only here, at creation - PF-S01.09's member-management page shows the
// invite link once, in this response, and never again (it is not re-readable from the pending list).
export type CreatedInvitation = Invitation & { token: string };
export type ActiveMember = {
  userId: string;
  email: string;
  role: string;
  employeeId?: string | null;
  vendorId?: string | null;
};
export type RoleCapabilities = {
  role: string;
  defaults: string[];
  grants: string[];
  revocations: string[];
  effective: string[];
};
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
  }
}
let csrfToken: string | undefined;
async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    cache: "no-store",
    credentials: "same-origin",
    headers: { Accept: "application/json", ...init.headers },
  });
  if (!response.ok) {
    const detail = (await response.json().catch(() => null)) as {
      title?: string;
      detail?: string;
    } | null;
    throw new ApiError(
      response.status,
      detail?.detail ?? detail?.title ?? `Request failed (${response.status})`,
    );
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>);
}
async function csrf() {
  const result = await request<{ token: string }>("/api/auth/csrf");
  csrfToken = result.token;
  return result.token;
}
async function mutation<T>(path: string, init: RequestInit = {}) {
  const token = csrfToken ?? (await csrf());
  return request<T>(path, { ...init, headers: { "X-CSRF-TOKEN": token, ...init.headers } });
}
// The bulk work routes take `items: [{ workId, version }]`. A missing token becomes version 0,
// which the server treats as a stale check and answers 409 — so the caller must pass a fresh
// token per id (re-read after any earlier mutation in the same flow).
function bulkItems(input: { workIds: string[]; concurrencyTokens?: Record<string, string> }) {
  return input.workIds.map((workId) => ({
    workId,
    version: Number(input.concurrencyTokens?.[workId] ?? 0),
  }));
}
function queryString(query: WorkListQuery) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(key, String(value));
  }
  const serialized = params.toString();
  return serialized ? `?${serialized}` : "";
}
// GET /api/work/ returns a flat page of work items; GET /api/work/{id} keeps the { item, version }
// envelope. normalizeWork accepts either and always yields a numeric `version` plus the string
// `rowVersion` the bulk-assignment payload builder still reads.
type WorkListResponse =
  | (WorkItem | WorkResponse)[]
  | { items: (WorkItem | WorkResponse)[]; totalCount?: number };
type WorkResponse = { item: Omit<WorkDetail, "version">; version: number };
async function listWork(query: WorkListQuery = {}): Promise<WorkListResult> {
  const response = await request<WorkListResponse>(`/api/work/${queryString(query)}`);
  const raw = Array.isArray(response) ? response : response.items;
  const items = raw.map(normalizeWork);
  const totalCount =
    !Array.isArray(response) && typeof response.totalCount === "number"
      ? response.totalCount
      : items.length;
  return { items, totalCount };
}
function normalizeWork(value: WorkItem | WorkResponse): WorkDetail {
  if ("item" in value)
    return { ...value.item, version: value.version, rowVersion: String(value.version) };
  const version = typeof value.version === "number" ? value.version : Number(value.rowVersion ?? 0);
  return { ...value, version, rowVersion: String(version) };
}
export const api = {
  session: () => request<Session>("/api/session"),
  auth: {
    async login(input: { organizationSlug: string; email: string; password: string }) {
      await csrf();
      await mutation<void>("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });
      csrfToken = undefined;
      await csrf();
    },
    requestPasswordRecovery(input: { organizationSlug: string; email: string }) {
      return mutation<void>("/api/auth/password-recovery/request", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });
    },
    resetPassword(input: {
      organizationSlug: string;
      email: string;
      token: string;
      newPassword: string;
    }) {
      return mutation<void>("/api/auth/password-recovery/reset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });
    },
    async logout() {
      await mutation<void>("/api/auth/logout", { method: "POST" });
      csrfToken = undefined;
    },
  },
  work: {
    list: listWork,
    async get(id: string) {
      return normalizeWork(await request<WorkResponse>(`/api/work/${id}`));
    },
    timeline: (id: string, residentVisibleOnly = false) =>
      request<TimelineEntry[]>(
        `/api/work/${id}/timeline${residentVisibleOnly ? "?residentVisibleOnly=true" : ""}`,
      ),
    async update(id: string, input: UpdateWorkInput) {
      return normalizeWork(
        await mutation<WorkResponse>(`/api/work/${id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      );
    },
    assignEmployee: (id: string, employeeId: string, version: number) =>
      mutation<{ changed: boolean }>(`/api/work/${id}/employee`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ employeeId, version }),
      }),
    assignVendor: (id: string, vendorId: string, version: number) =>
      mutation<{ changed: boolean }>(`/api/work/${id}/vendor`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ vendorId, version }),
      }),
    bulkAssignVendor: (input: {
      workIds: string[];
      vendorId: string;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/vendor", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ vendorId: input.vendorId, items: bulkItems(input) }),
      }),
    bulkAssignEmployee: (input: {
      workIds: string[];
      employeeId: string;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/employee", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ employeeId: input.employeeId, items: bulkItems(input) }),
      }),
    bulkSendResidentMessage: (input: { workIds: string[]; templateId: string }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/message", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    bulkSchedule: (input: {
      workIds: string[];
      scheduledStart: string;
      scheduledEnd?: string | null;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/schedule", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          scheduledStart: input.scheduledStart,
          scheduledEnd: input.scheduledEnd ?? null,
          items: bulkItems(input),
        }),
      }),
    bulkStatus: (input: {
      workIds: string[];
      status: string;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/status", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ status: input.status, items: bulkItems(input) }),
      }),
    bulkPriority: (input: {
      workIds: string[];
      priority: string;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/priority", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ priority: input.priority, items: bulkItems(input) }),
      }),
    bulkNote: (input: {
      workIds: string[];
      note: string;
      internal: boolean;
      concurrencyTokens?: Record<string, string>;
    }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/note", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          note: input.note,
          internal: input.internal,
          items: bulkItems(input),
        }),
      }),
    bulkReopen: (input: { workIds: string[]; concurrencyTokens?: Record<string, string> }) =>
      mutation<BulkAssignmentResult>("/api/work/bulk/reopen", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ items: bulkItems(input) }),
      }),
    // POST /api/work/{id}/message — queue one resident message for this work item's resident.
    // 202 { queued } on success; the caller classifies 4xx (no resident / no consent / etc.).
    sendMessage: (id: string, templateId: string) =>
      mutation<{ queued: boolean }>(`/api/work/${id}/message`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ templateId }),
      }),
    // Attachments (PF-7.01). Upload is multipart — no Content-Type header, fetch sets the
    // boundary. Download is a plain GET the browser handles; `downloadUrl` is a same-origin
    // href, cookies carry the session, no CSRF needed for the read.
    attachments: {
      list: (workId: string) => request<Attachment[]>(`/api/work/${workId}/attachments/`),
      upload: async (
        workId: string,
        file: File,
        options: { residentVisible: boolean; retainUntil?: string },
      ) => {
        const token = csrfToken ?? (await csrf());
        const form = new FormData();
        form.append("file", file);
        form.append("residentVisible", String(options.residentVisible));
        if (options.retainUntil) form.append("retainUntil", options.retainUntil);
        return request<Attachment>(`/api/work/${workId}/attachments/`, {
          method: "POST",
          headers: { "X-CSRF-TOKEN": token },
          body: form,
        });
      },
      remove: async (workId: string, attachmentId: string) => {
        const token = csrfToken ?? (await csrf());
        return request<void>(`/api/work/${workId}/attachments/${attachmentId}`, {
          method: "DELETE",
          headers: { "X-CSRF-TOKEN": token },
        });
      },
      downloadUrl: (workId: string, attachmentId: string) =>
        `/api/work/${workId}/attachments/${attachmentId}`,
    },
  },
  communication: {
    templates: { list: () => request<MessageTemplate[]>("/api/communication/templates/") },
  },
  vendors: { list: () => request<Vendor[]>("/api/vendors/") },
  employees: { list: () => request<Employee[]>("/api/employees/") },
  residents: { list: () => request<ResidentReference[]>("/api/residents/") },
  portfolios: {
    list: (q?: string) =>
      request<Portfolio[]>(`/api/portfolios/${q ? `?q=${encodeURIComponent(q)}` : ""}`),
    create: (input: { name: string }) =>
      mutation<Portfolio>("/api/portfolios/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    rename: (id: string, input: { name: string }) =>
      mutation<Portfolio>(`/api/portfolios/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    archive: (id: string) => mutation<void>(`/api/portfolios/${id}/archive`, { method: "POST" }),
    restore: (id: string) => mutation<void>(`/api/portfolios/${id}/restore`, { method: "POST" }),
  },
  properties: {
    list: (q?: string) =>
      request<PropertyReference[]>(`/api/properties/${q ? `?q=${encodeURIComponent(q)}` : ""}`),
    get: (id: string) => request<PropertyDetail>(`/api/properties/${id}`),
    contacts: {
      create: (
        propertyId: string,
        input: { fullName: string; role: string; email?: string | null; phone?: string | null },
      ) =>
        mutation<unknown>(`/api/properties/${propertyId}/contacts`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
    },
    documents: {
      create: (
        propertyId: string,
        input: { title: string; documentUrl: string; documentType?: string | null },
      ) =>
        mutation<unknown>(`/api/properties/${propertyId}/documents`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
    },
    create: (input: { portfolioId: string; name: string; timeZoneId: string }) =>
      mutation<PropertyReference>("/api/properties/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    update: (id: string, input: { portfolioId: string; name: string; timeZoneId: string }) =>
      mutation<PropertyReference>(`/api/properties/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    archive: (id: string) => mutation<void>(`/api/properties/${id}/archive`, { method: "POST" }),
    restore: (id: string) => mutation<void>(`/api/properties/${id}/restore`, { method: "POST" }),
    buildings: {
      create: (propertyId: string, input: { name: string }) =>
        mutation<{ id: string; propertyId: string; name: string }>(
          `/api/properties/${propertyId}/buildings`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(input),
          },
        ),
      rename: (propertyId: string, buildingId: string, input: { name: string }) =>
        mutation<{ id: string; propertyId: string; name: string }>(
          `/api/properties/${propertyId}/buildings/${buildingId}`,
          {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(input),
          },
        ),
    },
    amenities: {
      create: (propertyId: string, input: { name: string; details?: string | null }) =>
        mutation<unknown>(`/api/properties/${propertyId}/amenities`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      update: (
        propertyId: string,
        amenityId: string,
        input: { name: string; details?: string | null },
      ) =>
        mutation<unknown>(`/api/properties/${propertyId}/amenities/${amenityId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      archive: (propertyId: string, amenityId: string) =>
        mutation<void>(`/api/properties/${propertyId}/amenities/${amenityId}/archive`, {
          method: "POST",
        }),
    },
    spaces: {
      create: (propertyId: string, input: { buildingId?: string | null; code: string }) =>
        mutation<{ id: string; propertyId: string; buildingId?: string | null; code: string }>(
          `/api/properties/${propertyId}/spaces`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(input),
          },
        ),
      update: (
        propertyId: string,
        spaceId: string,
        input: { buildingId?: string | null; code: string },
      ) =>
        mutation<{ id: string; propertyId: string; buildingId?: string | null; code: string }>(
          `/api/properties/${propertyId}/spaces/${spaceId}`,
          {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(input),
          },
        ),
    },
  },
  marketing: {
    listings: {
      list: (propertyId?: string) =>
        request<Listing[]>(
          `/api/marketing/listings/${propertyId ? `?propertyId=${encodeURIComponent(propertyId)}` : ""}`,
        ),
      create: (input: {
        propertyId: string;
        spaceId?: string | null;
        headline: string;
        description?: string | null;
        availableOn?: string | null;
        monthlyRent?: number | null;
      }) =>
        mutation<Listing>("/api/marketing/listings/", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      publish: (id: string) =>
        mutation<Listing>(`/api/marketing/listings/${id}/publish`, { method: "POST" }),
      unpublish: (id: string) =>
        mutation<Listing>(`/api/marketing/listings/${id}/unpublish`, { method: "POST" }),
      inquiries: (id: string) => request<Inquiry[]>(`/api/marketing/listings/${id}/inquiries`),
      applicants: (id: string) => request<Applicant[]>(`/api/marketing/listings/${id}/applicants`),
      createApplicant: (id: string, inquiryId: string) =>
        mutation<Applicant>(`/api/marketing/listings/${id}/applicants`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ inquiryId }),
        }),
      applicantStatus: (id: string, applicantId: string, status: Applicant["status"]) =>
        mutation<Applicant>(`/api/marketing/listings/${id}/applicants/${applicantId}/status`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ status }),
        }),
      createInquiry: (
        id: string,
        input: {
          prospectName: string;
          email: string;
          phone?: string | null;
          message?: string | null;
          leadSource?: string | null;
        },
      ) =>
        mutation<Inquiry>(`/api/marketing/listings/${id}/inquiries`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      inquiryStatus: (id: string, inquiryId: string, status: Inquiry["status"]) =>
        mutation<Inquiry>(`/api/marketing/listings/${id}/inquiries/${inquiryId}/status`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ status }),
        }),
      showings: (id: string) => request<Showing[]>(`/api/marketing/listings/${id}/showings`),
      createShowing: (id: string, input: { prospectName: string; scheduledAt: string }) =>
        mutation<Showing>(`/api/marketing/listings/${id}/showings`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      showingStatus: (id: string, showingId: string, status: Showing["status"]) =>
        mutation<Showing>(`/api/marketing/listings/${id}/showings/${showingId}/status`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ status }),
        }),
    },
  },
  leasing: {
    leases: {
      list: () => request<Lease[]>("/api/leasing/leases/"),
      create: (input: {
        residentId: string;
        spaceId: string;
        startsOn: string;
        endsOn: string;
        monthlyRent: number;
        securityDeposit?: number | null;
      }) =>
        mutation<Lease>("/api/leasing/leases/", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      activate: (id: string) =>
        mutation<Lease>(`/api/leasing/leases/${id}/activate`, { method: "POST" }),
      renew: (id: string, input: { endsOn: string; monthlyRent: number }) =>
        mutation<Lease>(`/api/leasing/leases/${id}/renew`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      notice: (
        id: string,
        input: {
          type: "Renewal" | "MoveOut";
          noticeDate: string;
          moveOutOn: string;
          notes?: string | null;
        },
      ) =>
        mutation<unknown>(`/api/leasing/leases/${id}/notice`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      moveOut: (id: string, moveOutOn: string) =>
        mutation<Lease>(`/api/leasing/leases/${id}/move-out`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ moveOutOn }),
        }),
      transfer: (id: string, input: { residentId: string; spaceId: string; effectiveOn: string }) =>
        mutation<Lease>(`/api/leasing/leases/${id}/transfer`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      notices: (id: string) => request<LeaseNotice[]>(`/api/leasing/leases/${id}/notices`),
      parties: (id: string) => request<LeaseParty[]>(`/api/leasing/leases/${id}/parties`),
      addParty: (id: string, input: { fullName: string; role: string; email?: string | null }) =>
        mutation<LeaseParty>(`/api/leasing/leases/${id}/parties`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      documents: (id: string) =>
        request<PortalLeaseDocument[]>(`/api/leasing/leases/${id}/documents`),
      addDocument: (
        id: string,
        input: { title: string; documentUrl: string; residentVisible: boolean },
      ) =>
        mutation<PortalLeaseDocument>(`/api/leasing/leases/${id}/documents`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      sendDocument: (id: string, documentId: string) =>
        mutation<PortalLeaseDocument>(`/api/leasing/leases/${id}/documents/${documentId}/send`, {
          method: "POST",
        }),
      signDocument: (id: string, documentId: string, signedBy: string) =>
        mutation<PortalLeaseDocument>(`/api/leasing/leases/${id}/documents/${documentId}/sign`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ signedBy }),
        }),
      charges: (id: string) => request<LeaseCharge[]>(`/api/leasing/leases/${id}/charges`),
      addCharge: (
        id: string,
        input: {
          type: "Recurring" | "OneTime";
          description: string;
          amount: number;
          dueOn: string;
        },
      ) =>
        mutation<LeaseCharge>(`/api/leasing/leases/${id}/charges`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
    },
  },
  portal: {
    me: () => request<PortalSummary>("/api/portal/me"),
    documents: () => request<PortalDocument[]>("/api/portal/documents"),
    leaseDocuments: () => request<PortalLeaseDocument[]>("/api/portal/lease-documents"),
    payments: () => request<ResidentPayment[]>("/api/portal/payments"),
    charges: () => request<LeaseCharge[]>("/api/portal/charges"),
    submitPayment: (input: {
      leaseId: string;
      chargeId?: string | null;
      amount: number;
      dueOn: string;
      reference?: string | null;
    }) =>
      mutation<ResidentPayment>("/api/portal/payments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    announcements: () => request<PortalAnnouncement[]>("/api/portal/announcements"),
    updateProfile: (input: { fullName: string; email?: string | null; phone?: string | null }) =>
      mutation<PortalSummary["resident"]>("/api/portal/profile", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    updatePreferences: (input: { emailEnabled: boolean; smsEnabled: boolean }) =>
      mutation<{ emailEnabled: boolean; smsEnabled: boolean }>("/api/portal/preferences", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    createHouseholdMember: (input: {
      fullName: string;
      relationship: string;
      email?: string | null;
    }) =>
      mutation<{ id: string; fullName: string; relationship: string; email?: string | null }>(
        "/api/portal/household-members",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        },
      ),
    createServiceRequest: (input: {
      spaceId: string;
      title: string;
      description?: string | null;
    }) =>
      mutation<{ id: string; title: string; status: string; residentVisibleNotes?: string | null }>(
        "/api/portal/service-requests",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        },
      ),
  },
  announcements: {
    list: () => request<Announcement[]>("/api/announcements/"),
    create: (input: { title: string; body: string; expiresAt?: string | null }) =>
      mutation<Announcement>("/api/announcements/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    publish: (id: string) =>
      mutation<Announcement>(`/api/announcements/${id}/publish`, { method: "POST" }),
    archive: (id: string) => mutation<void>(`/api/announcements/${id}/archive`, { method: "POST" }),
  },
  assets: {
    list: (propertyId?: string) =>
      request<Asset[]>(`/api/assets/${propertyId ? `?propertyId=${propertyId}` : ""}`),
    get: (id: string) => request<Asset>(`/api/assets/${id}`),
    history: (id: string) => request<AssetHistory>(`/api/assets/${id}/history`),
    repeatRepair: (id: string, categoryId?: string | null) =>
      request<RepeatRepairAssessment>(
        `/api/assets/${id}/repeat-repair${categoryId ? `?categoryId=${encodeURIComponent(categoryId)}` : ""}`,
      ),
  },
  attention: {
    get: () => request<AttentionQueue>("/api/attention"),
  },
  integrations: {
    sources: () => request<IntegrationSource[]>("/api/integrations/sources"),
    list: () => request<IntegrationHealth[]>("/api/integrations"),
    records: (id: string, page = 1, pageSize = 100) =>
      request<IntegrationRecordsPage>(
        `/api/integrations/${id}/records?page=${page}&pageSize=${pageSize}`,
      ),
    create: (input: { sourceSystem: string; displayName: string }) =>
      mutation<{ id: string }>("/api/integrations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    sync: (id: string) => mutation<SyncReport>(`/api/integrations/${id}/sync`, { method: "POST" }),
    setEnabled: (id: string, enabled: boolean) =>
      mutation<void>(`/api/integrations/${id}/${enabled ? "enable" : "disable"}`, {
        method: "POST",
      }),
  },
  search: (term: string, limit = 12) =>
    request<SearchHit[]>(`/api/search?q=${encodeURIComponent(term)}&limit=${limit}`),
  messageTemplates: {
    available: () => request<MessageTemplate[]>("/api/communication/templates/available"),
  },
  categories: { list: () => request<Category[]>("/api/categories/") },
  automationRules: {
    list: () => request<AutomationRule[]>("/api/automation/rules/"),
    create: (input: { name: string; trigger: string; conditions: unknown[]; actions: unknown[] }) =>
      mutation<AutomationRule>("/api/automation/rules/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    setEnabled: (id: string, enabled: boolean) =>
      mutation<AutomationRule>(`/api/automation/rules/${id}/enabled`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ enabled }),
      }),
  },
  savedViews: {
    list: () => request<SavedView[]>("/api/saved-views/"),
    create: (input: { name: string; filters: unknown; columns?: unknown; isDefault?: boolean }) =>
      mutation<SavedView>("/api/saved-views/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...input,
          columns: input.columns ?? {},
          isDefault: input.isDefault ?? false,
        }),
      }),
    delete: (id: string) => mutation<void>(`/api/saved-views/${id}`, { method: "DELETE" }),
  },
  // PF-S01.09: organization membership administration (invitations, roles, members).
  invitations: {
    list: () => request<Invitation[]>("/api/organizations/current/invitations"),
    create: (input: { email: string; role: string }) =>
      mutation<CreatedInvitation>("/api/organizations/current/invitations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    // Anonymous - the acceptor has no session yet. Still goes through `mutation`, which fetches
    // its own CSRF token first; the app's antiforgery middleware applies to every non-GET /api/*
    // route regardless of authentication.
    accept: (token: string, password: string) =>
      mutation<void>(`/api/invitations/${encodeURIComponent(token)}/accept`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password }),
      }),
  },
  roles: {
    list: () => request<RoleCapabilities[]>("/api/organizations/current/roles"),
  },
  members: {
    list: () => request<ActiveMember[]>("/api/organizations/current/members"),
    changeRole: (userId: string, role: string) =>
      mutation<void>(`/api/organizations/current/members/${userId}/role`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ role }),
      }),
    remove: (userId: string) =>
      mutation<void>(`/api/organizations/current/members/${userId}`, { method: "DELETE" }),
  },
};
