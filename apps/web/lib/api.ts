export type Session = {
  userId: string;
  organizationId: string;
  role: string;
  capabilities: string[];
};
export type WorkItem = {
  id: string;
  title: string;
  status: string;
  priority: string;
  categoryId?: string | null;
  categoryName?: string | null;
  propertyId?: string | null;
  propertyName?: string | null;
  vendorId?: string | null;
  vendorName?: string | null;
  dueDate?: string | null;
  createdAt?: string | null;
  rowVersion?: string | null;
};
export type WorkDetail = WorkItem & {
  description?: string | null;
  workType?: string;
  buildingId?: string | null;
  spaceId?: string | null;
  residentId?: string | null;
  employeeId?: string | null;
  scheduledStart?: string | null;
  scheduledEnd?: string | null;
  completedAt?: string | null;
  cost?: number | null;
  internalNotes?: string | null;
  residentVisibleNotes?: string | null;
  version: number;
};
export type TimelineEntry = {
  id: string;
  eventType?: string | null;
  occurredAt: string;
  actorId?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
  relatedObjectType?: string | null;
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
export type WorkListQuery = {
  search?: string;
  status?: string;
  priority?: string;
  categoryId?: string;
  propertyId?: string;
  sort?: string;
  descending?: boolean;
  page?: number;
  pageSize?: number;
};
export type WorkListResult = { items: WorkItem[]; totalCount: number };
export type BulkAssignmentResult = { changed: number; unchanged: number; total: number };
export type Category = { id: string; name: string; isArchived: boolean; sortOrder: number };
export type SavedView = {
  id: string;
  name: string;
  filters: string;
  columns: string;
  isDefault: boolean;
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
function queryString(query: WorkListQuery) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(key, String(value));
  }
  const serialized = params.toString();
  return serialized ? `?${serialized}` : "";
}
async function listWork(query: WorkListQuery = {}): Promise<WorkListResult> {
  const response = await request<WorkItem[] | WorkListResult | { items: WorkResponse[] }>(
    `/api/work/${queryString(query)}`,
  );
  if (Array.isArray(response)) return { items: response, totalCount: response.length };
  const items = response.items.map(normalizeWork);
  return { items, totalCount: "totalCount" in response ? response.totalCount : items.length };
}
type WorkResponse = { item: Omit<WorkDetail, "version">; version: number };
function normalizeWork(value: WorkItem | WorkResponse): WorkDetail {
  if ("item" in value)
    return { ...value.item, version: value.version, rowVersion: String(value.version) };
  return { ...value, version: Number(value.rowVersion ?? 0) };
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
    timeline: (id: string) => request<TimelineEntry[]>(`/api/work/${id}/timeline`),
    async update(id: string, input: UpdateWorkInput) {
      return normalizeWork(
        await mutation<WorkResponse>(`/api/work/${id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      );
    },
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
    }) => {
      const items = input.workIds.map((workId) => ({
        workId,
        version: Number(input.concurrencyTokens?.[workId] ?? 0),
      }));
      return mutation<BulkAssignmentResult>("/api/work/bulk/vendor", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ vendorId: input.vendorId, items }),
      });
    },
  },
  vendors: { list: () => request<Vendor[]>("/api/vendors/") },
  categories: { list: () => request<Category[]>("/api/categories/") },
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
};
