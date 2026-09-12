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
// FS-S05 rental applications. Enums arrive as strings (Program.cs registers
// JsonStringEnumConverter), so the union types below are the wire shape, not a mapping.
export type ApplicationStatus =
  | "Draft"
  | "Submitted"
  | "ConsentGranted"
  | "Screening"
  | "UnderReview"
  | "Approved"
  | "Denied"
  | "Withdrawn";
export type ApplicantRole = "Primary" | "CoApplicant" | "Guarantor";
export type ApplicationConsentType =
  | "BackgroundCheck"
  | "CreditCheck"
  | "EvictionHistory"
  | "IncomeVerification";
export type RecordedConsentDecision = "Granted" | "Revoked";
export type ScreeningRequestStatus = "Pending" | "InFlight" | "Completed" | "Abandoned";
// Unavailable is the provider-outage marker and is never persisted as a verdict — an outage is a
// 503 with nothing written (docs/api.md, "Provider outage"). It is in the union because the
// vocabulary is shared with the provider port, not because a row can hold it.
export type ScreeningRecommendation = "Pass" | "Review" | "Fail" | "Unavailable";

// Masking is by ABSENCE, not by null: without Applications.ReadPii the API omits `email`,
// `phone`, `monthlyIncome` and `employmentStatus` entirely. Optional properties are what models
// that — `"monthlyIncome" in row` separates "you may not see this" from "no value is held", and
// the panel renders those two facts differently. Do not widen these to `| null` only.
export type ApplicationApplicantRow = {
  id: string;
  applicantId: string;
  name: string;
  role: ApplicantRole;
  email?: string;
  phone?: string | null;
  monthlyIncome?: number | null;
  employmentStatus?: string | null;
};
// `recommendation` is present in BOTH shapes and null until a verdict lands; `score`, `summary`
// and `receivedAt` are the masked ones.
export type ScreeningRequestRow = {
  requestId: string;
  applicantId: string;
  status: ScreeningRequestStatus;
  attempts: number;
  lastAttemptedAt?: string | null;
  lastError?: string | null;
  completedAt?: string | null;
  recommendation?: ScreeningRecommendation | null;
  score?: number | null;
  summary?: string | null;
  receivedAt?: string | null;
};
export type RentalApplication = {
  id: string;
  listingId: string;
  status: ApplicationStatus;
  submittedAt?: string | null;
  decidedAt?: string | null;
  applicants: ApplicationApplicantRow[];
  screening: ScreeningRequestRow[];
};
export type EffectiveConsent = {
  applicantId: string;
  consentType: ApplicationConsentType;
  decision: RecordedConsentDecision;
  recordedAt: string;
  recordedBy: string;
  source: string;
};
export type ApplicationConsentRow = EffectiveConsent & {
  id: string;
  applicationId: string;
  allowsScreening: boolean;
};
// The whole append-only row set plus its reduction: "consent was held when screening ran" is
// answered by `history`, not by `effective`.
export type ApplicationConsentLog = {
  applicationId: string;
  effective: EffectiveConsent[];
  history: ApplicationConsentRow[];
};
export type ApplicationDecision = {
  id: string;
  applicationId: string;
  outcome: "Approved" | "Denied";
  reason: string;
  note?: string | null;
  decidedBy: string;
  decidedAt: string;
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
  // PF-S19: a conflict is deliberately NOT a failure, so without this badge a connection raising
  // the same unmapped-status conflict on every run looks identical to a clean one.
  openConflicts: number;
};
export type IntegrationEntityKind =
  | "Property"
  | "Space"
  | "Occupancy"
  | "WorkOrder"
  | "Asset"
  | "Resident"
  | "Building";
// Conflicted and Retired arrived with PF-S19. Retired never means the PropFlow row was deleted —
// the link is retired and the row is left alone.
export type IntegrationSyncState = "Pending" | "Synced" | "Failed" | "Conflicted" | "Retired";
export type IntegrationRecord = {
  id: string;
  connectionId: string;
  kind: IntegrationEntityKind;
  externalId: string;
  syncState: IntegrationSyncState;
  lastSeenAt?: string | null;
  lastError?: string | null;
  // What the source last said versus what was last written into PropFlow. The two differing is
  // what makes a replayed sync converge rather than lose a change.
  contentHash?: string | null;
  reconciledHash?: string | null;
  internalId?: string | null;
  lastReconciledAt?: string | null;
  lastRunId?: string | null;
};
export type IntegrationRecordsPage = {
  items: IntegrationRecord[];
  totalCount: number;
  page: number;
  pageSize: number;
};
export type SyncReport = {
  outcome: "Completed" | "Failed" | "NotFound" | "Disabled" | "AlreadyRunning";
  // PF-S19.05 changed what these count: PropFlow rows, not external record links. `conflicted`
  // being non-zero on a first sync is the intended connect → review → promote flow, not a failure.
  seen: number;
  added: number;
  updated: number;
  failed: number;
  conflicted: number;
  retired: number;
  error?: string | null;
  runId?: string | null;
};
// --- PF-S19 conflict queue -------------------------------------------------------------------
export type ConflictReason =
  | "UnmappedValue"
  | "MissingRequiredMapping"
  | "MissingParentLink"
  | "UpstreamDisappearance"
  | "AmbiguousMatch"
  | "ValidationRefusal";
export type ConflictStatus = "Open" | "Resolved" | "Ignored";
export type IntegrationConflict = {
  id: string;
  connectionId: string;
  kind: IntegrationEntityKind;
  externalId: string;
  reason: ConflictReason;
  field: string;
  observedValue?: string | null;
  currentValue?: string | null;
  detail?: string | null;
  status: ConflictStatus;
  firstSeenInRunId: string;
  lastSeenInRunId: string;
  firstSeenAt: string;
  lastSeenAt: string;
  observationCount: number;
  resolvedByUserId?: string | null;
  resolvedAt?: string | null;
  resolutionNote?: string | null;
  // False once the connection's most recent COMPLETED run no longer detected the divergence.
  //
  // The reconciler re-observes a conflict it still sees and stops observing one it cannot, but it
  // NEVER closes a row on its own: "I no longer detect it" and "a human decided" are different
  // facts, and only the second belongs in resolvedByUserId. So a divergence that has actually
  // been fixed leaves its row Open, and the queue would appear never to empty after a successful
  // fix. This flag — and the page's staleCount — are how the UI tells that story honestly
  // instead of auto-closing rows with a synthetic system actor.
  seenInLatestRun: boolean;
};
// openCount and staleCount are the whole connection's totals, not the page's, so paging through
// the queue must never change them.
export type ConflictsPage = {
  items: IntegrationConflict[];
  totalCount: number;
  openCount: number;
  staleCount: number;
  page: number;
  pageSize: number;
};
// --- PF-S19 mapping profiles -----------------------------------------------------------------
// A profile is created ReportOnly and there is no way to create one that is not. In ReportOnly a
// sync raises conflicts and writes NOTHING to the operations schema; promotion is a separate,
// refusable step.
export type MappingMode = "ReportOnly" | "AutoApply";
// Error blocks promotion. Unmapped is informational — a canonical field PropFlow has nowhere to
// put — and never blocks it.
export type MappingIssueSeverity = "Error" | "Unmapped";
export type MappingIssue = { severity: MappingIssueSeverity; field: string; reason: string };
export type MappingSourceField = "WorkOrderStatus" | "AssetKind" | "PropertyTimeZone";
export type MappingRule = {
  id: string;
  sourceField: MappingSourceField;
  sourceValue: string;
  targetValue: string;
};
export type MappingProfile = {
  id: string;
  connectionId: string;
  kind: IntegrationEntityKind;
  mode: MappingMode;
  targetPortfolioId?: string | null;
  defaultCreatorId?: string | null;
  defaultTimeZoneId?: string | null;
  createdAt: string;
  updatedAt: string;
  rules: MappingRule[];
  issues: MappingIssue[];
  // The same answer the promote endpoint will give, computed from the pure MappingProfile.
  // Validate() — so the panel can disable promotion without a speculative round-trip.
  canAutoApply: boolean;
  availableSourceFields: MappingSourceField[];
};
// --- PF-S19 run history ----------------------------------------------------------------------
export type SyncTrigger = "Manual" | "Scheduled" | "Retry";
export type SyncRunStatus = "Running" | "Completed" | "Failed";
export type SyncRun = {
  id: string;
  connectionId: string;
  trigger: SyncTrigger;
  attemptNumber: number;
  status: SyncRunStatus;
  startedAt: string;
  heartbeatAt: string;
  completedAt?: string | null;
  seen: number;
  added: number;
  updated: number;
  failed: number;
  conflicted: number;
  error?: string | null;
  snapshotHash?: string | null;
};
export type SyncRunsPage = {
  items: SyncRun[];
  totalCount: number;
  page: number;
  pageSize: number;
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
// PF-S03.08: settings admin UI for the PF-S03 configuration primitives (custom fields,
// numbering, business hours, notification preferences). AppliesTo is a closed vocabulary with
// one value today (`WorkItem`) - the UI hardcodes it rather than offering a chooser with one
// option, the same call CustomFieldRequest's server-side default makes.
export type CustomFieldType = "Text" | "Number" | "Date" | "Boolean" | "SingleSelect";
export type CustomFieldDefinition = {
  id: string;
  key: string;
  name: string;
  appliesTo: string;
  fieldType: CustomFieldType;
  options: string[];
  isRequired: boolean;
  sortOrder: number;
  isArchived: boolean;
  createdAt: string;
};
export type NumberingScheme = {
  appliesTo: string;
  prefix: string;
  width: number;
  nextValue: number;
  nextFormatted: string;
};
export type BusinessHoursWindow = { day: string; open: string | null; close: string | null };
export type OrganizationSettings = {
  defaultTimeZoneId: string | null;
  businessHours: BusinessHoursWindow[];
};
export type NotificationEventType = "WorkAssigned" | "AutomationApplied" | "InvitationReceived";
export type NotificationPreference = { eventType: NotificationEventType; enabled: boolean };
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
    // The parsed ProblemDetails body, when there was one. Kept because some refusals carry
    // structured extensions the message cannot express: POST
    // /api/integrations/{id}/mappings/{kind}/promote answers 409 with an `issues` array naming
    // exactly what to fix (src/PropFlow.Api/IntegrationEndpoints.cs:190-192). Collapsing that to
    // a title would throw away the entire point of the promotion guard rail.
    public readonly body?: unknown,
  ) {
    super(message);
  }
}
/**
 * Read a named array extension off an ApiError's ProblemDetails body.
 *
 * Returns [] for anything that is not that shape, so a caller never has to defend against a
 * refusal that arrived without the extension — a proxy error page, a 502, an older API.
 */
export function problemArray<T>(error: unknown, key: string): T[] {
  if (!(error instanceof ApiError)) return [];
  if (typeof error.body !== "object" || error.body === null) return [];
  const value = (error.body as Record<string, unknown>)[key];
  return Array.isArray(value) ? (value as T[]) : [];
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
      detail,
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
  // FS-S05. Every route needs Applications.Manage; `pii` additionally needs
  // Applications.ReadPii and answers 403 without it. `startScreening` and `retryScreening`
  // answer 503 on a provider outage with nothing written — callers must tell that apart from a
  // 409 (a refused transition) and from a Fail verdict, which is a real answer about a person.
  applications: {
    list: (listingId?: string, status?: ApplicationStatus) => {
      const params = new URLSearchParams();
      if (listingId) params.set("listingId", listingId);
      if (status) params.set("status", status);
      const query = params.toString();
      return request<RentalApplication[]>(`/api/applications/${query ? `?${query}` : ""}`);
    },
    get: (id: string) => request<RentalApplication>(`/api/applications/${id}`),
    pii: (id: string) => request<RentalApplication>(`/api/applications/${id}/pii`),
    create: (listingId: string) =>
      mutation<RentalApplication>("/api/applications/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ listingId }),
      }),
    addApplicant: (
      id: string,
      input: {
        applicantId: string;
        role: ApplicantRole;
        monthlyIncome?: number | null;
        employmentStatus?: string | null;
      },
    ) =>
      mutation<RentalApplication>(`/api/applications/${id}/applicants`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    submit: (id: string) =>
      mutation<RentalApplication>(`/api/applications/${id}/submit`, { method: "POST" }),
    consent: (id: string) => request<ApplicationConsentLog>(`/api/applications/${id}/consent`),
    recordConsent: (
      id: string,
      input: {
        applicantId: string;
        consentType: ApplicationConsentType;
        decision: RecordedConsentDecision;
        source: string;
      },
    ) =>
      mutation<{ consent: ApplicationConsentRow; applicationStatus: ApplicationStatus }>(
        `/api/applications/${id}/consent`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        },
      ),
    withdraw: (id: string) =>
      mutation<RentalApplication>(`/api/applications/${id}/withdraw`, { method: "POST" }),
    screening: (id: string) => request<ScreeningRequestRow[]>(`/api/applications/${id}/screening`),
    startScreening: (id: string) =>
      mutation<RentalApplication>(`/api/applications/${id}/screening`, { method: "POST" }),
    retryScreening: (id: string, requestId: string) =>
      mutation<RentalApplication>(`/api/applications/${id}/screening/${requestId}/retry`, {
        method: "POST",
      }),
    approve: (id: string, input: { reason: string; note?: string | null }) =>
      mutation<{ decision: ApplicationDecision; applicationStatus: ApplicationStatus }>(
        `/api/applications/${id}/approve`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        },
      ),
    deny: (id: string, input: { reason: string; note?: string | null }) =>
      mutation<{ decision: ApplicationDecision; applicationStatus: ApplicationStatus }>(
        `/api/applications/${id}/deny`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        },
      ),
    decisions: (id: string) => request<ApplicationDecision[]>(`/api/applications/${id}/decisions`),
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
    // PF-S19.09. A conflict is closed by a status transition and never deleted: the runtime role
    // holds no DELETE grant on integrations."Conflicts", because a resolved conflict is the
    // record that a human looked at a divergence and made a call.
    conflicts: (
      id: string,
      filter: {
        status?: ConflictStatus;
        kind?: IntegrationEntityKind;
        reason?: ConflictReason;
      } = {},
      page = 1,
      pageSize = 100,
    ) => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (filter.status) params.set("status", filter.status);
      if (filter.kind) params.set("kind", filter.kind);
      if (filter.reason) params.set("reason", filter.reason);
      return request<ConflictsPage>(`/api/integrations/${id}/conflicts?${params}`);
    },
    closeConflict: (id: string, conflictId: string, ignore: boolean, note?: string | null) =>
      mutation<void>(
        `/api/integrations/${id}/conflicts/${conflictId}/${ignore ? "ignore" : "resolve"}`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ note: note?.trim() ? note.trim() : null }),
        },
      ),
    mappings: (id: string) => request<MappingProfile[]>(`/api/integrations/${id}/mappings`),
    saveMapping: (
      id: string,
      kind: IntegrationEntityKind,
      input: {
        targetPortfolioId?: string | null;
        defaultCreatorId?: string | null;
        defaultTimeZoneId?: string | null;
      },
    ) =>
      mutation<{ id: string }>(`/api/integrations/${id}/mappings/${kind}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    addMappingRule: (
      id: string,
      kind: IntegrationEntityKind,
      input: { sourceField: MappingSourceField; sourceValue: string; targetValue: string },
    ) =>
      mutation<{ id: string }>(`/api/integrations/${id}/mappings/${kind}/rules`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    removeMappingRule: (id: string, kind: IntegrationEntityKind, ruleId: string) =>
      mutation<void>(`/api/integrations/${id}/mappings/${kind}/rules/${ruleId}`, {
        method: "DELETE",
      }),
    // Refuses with 409 while the profile has Error issues, carrying the whole issue list in the
    // ProblemDetails body. Callers read it with problemArray<MappingIssue>(error, "issues") —
    // rendering those is the difference between a guard rail and a bare "no".
    promoteMapping: (id: string, kind: IntegrationEntityKind) =>
      mutation<{ mode: MappingMode; issues: MappingIssue[] }>(
        `/api/integrations/${id}/mappings/${kind}/promote`,
        { method: "POST" },
      ),
    revertMappingToReportOnly: (id: string, kind: IntegrationEntityKind) =>
      mutation<void>(`/api/integrations/${id}/mappings/${kind}/report-only`, { method: "POST" }),
    runs: (id: string, page = 1, pageSize = 25) =>
      request<SyncRunsPage>(`/api/integrations/${id}/runs?page=${page}&pageSize=${pageSize}`),
    // Retires the LINK. The PropFlow row it reconciled into is deliberately left untouched.
    retireRecord: (id: string, recordId: string) =>
      mutation<void>(`/api/integrations/${id}/records/${recordId}/retire`, { method: "POST" }),
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
  // PF-S03.08: settings admin UI for custom fields, numbering, business hours, and
  // notification preferences. AppliesTo is hardcoded to "WorkItem" - the only value the closed
  // ConfigurationEntityType vocabulary defines today.
  customFields: {
    list: () => request<CustomFieldDefinition[]>("/api/settings/custom-fields/"),
    create: (input: {
      key: string;
      name: string;
      fieldType: CustomFieldType;
      options?: string[] | null;
      isRequired: boolean;
      sortOrder: number;
    }) =>
      mutation<CustomFieldDefinition>("/api/settings/custom-fields/", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...input, appliesTo: "WorkItem", options: input.options ?? null }),
      }),
    update: (
      id: string,
      input: { name: string; options?: string[] | null; isRequired: boolean; sortOrder: number },
    ) =>
      mutation<CustomFieldDefinition>(`/api/settings/custom-fields/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...input, options: input.options ?? null }),
      }),
    archive: (id: string) =>
      mutation<void>(`/api/settings/custom-fields/${id}/archive`, { method: "POST" }),
  },
  numbering: {
    list: () => request<NumberingScheme[]>("/api/settings/numbering/"),
    configure: (appliesTo: string, input: { prefix?: string | null; width: number }) =>
      mutation<NumberingScheme>(`/api/settings/numbering/${appliesTo}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
  },
  organizationSettings: {
    get: () => request<OrganizationSettings>("/api/settings/organization/"),
    update: (input: OrganizationSettings) =>
      mutation<OrganizationSettings>("/api/settings/organization/", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
  },
  notificationPreferences: {
    list: () => request<NotificationPreference[]>("/api/settings/notification-preferences/"),
    setEnabled: (eventType: NotificationEventType, enabled: boolean) =>
      mutation<NotificationPreference>(`/api/settings/notification-preferences/${eventType}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ enabled }),
      }),
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
