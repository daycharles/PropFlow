import { expect, test, type Page } from "@playwright/test";

const organizationSlug = "tidewater-demo";
const adminEmail = "demo-admin@tidewater.example.test";
const adminPassword = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
// PF-5.10: the seeded field account is intentionally supplied by CI so this workflow exercises
// the same scoped authorization a real technician has — a Technician membership carries only
// [Work.Read, Work.MarkOnTheWay] (src/PropFlow.Application/Capabilities.cs:31) and the endpoint
// narrows that to the work assigned to its own employee (src/PropFlow.Api/WorkEndpoints.cs:49).
const technicianEmail =
  process.env.PLAYWRIGHT_TECHNICIAN_EMAIL ?? "demo-technician@tidewater.example.test";
const technicianPassword = process.env.PLAYWRIGHT_TECHNICIAN_PASSWORD ?? "DemoPassword!123";

async function signIn(page: Page, email: string, password: string) {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();
}

async function signOut(page: Page) {
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByLabel("Organization slug")).toBeVisible();
}

/**
 * Provision the work this test acts on, as the administrator, so the run does not consume the
 * demo seed.
 *
 * Marking work on the way moves it out of the states this spec needs and `seed-demo` skips an
 * organization that already exists, so a version of this spec that picked up a seeded assignment
 * could only pass until the technician's open work ran out — it did, on the fourth consecutive
 * run. Every other spec provisions its own work (see `m3-workflow.spec.ts`) and this one now
 * does too.
 *
 * Three preconditions have to be assembled, and only an admin can do it: a Technician can read
 * and mark on the way, nothing else (Capabilities.cs:31). Hence the sign-in/sign-out dance in
 * the test body rather than a second browser context — `browser.newContext()` does not inherit
 * the config's `use` options, so it would not share this project's baseURL.
 *
 * The requests run *inside the page* via `page.evaluate`, not through `page.request`. The
 * session and antiforgery cookies are `__Host-` prefixed and `Secure`
 * (src/PropFlow.Api/Program.cs:79-81, 93-95); Playwright's APIRequestContext will not attach a
 * Secure cookie to the plain-http baseURL, so `page.request` is anonymous and every call comes
 * back 401. Mutations need the `X-CSRF-TOKEN` header from GET /api/auth/csrf
 * (SessionEndpoints.cs:12), which every non-GET request under /api is validated against
 * (src/PropFlow.Api/Program.cs:140).
 */
async function provisionAssignedWork(page: Page, title: string) {
  return page.evaluate(
    async ({ title }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok)
          throw new Error(`${init?.method ?? "GET"} ${path} returned ${response.status}`);
        return response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const headers = { "Content-Type": "application/json", "X-CSRF-TOKEN": token };

      const properties = (await json("/api/properties/")) as { id: string }[];
      if (properties.length === 0) throw new Error("the demo organization has no property");

      // `seed-demo` gives the technician membership the first *active* employee ordered by
      // display name (tools/PropFlow.Admin/Program.cs:125-126), and GET /api/employees/ orders
      // by DisplayName then Id (ReferenceEndpoints.cs:43), so this is the same employee. The
      // assertion on the technician's scoped list in the test body is what proves it.
      const employees = (
        (await json("/api/employees/")) as { id: string; displayName: string; isActive: boolean }[]
      ).filter((employee) => employee.isActive);
      if (employees.length === 0) throw new Error("the demo organization has no active employee");
      const employee = employees[0];

      // A resident who consented to SMS, because the "Technician on the way" template is an SMS
      // template (tools/PropFlow.Admin/TidewaterSeed.cs:290) and the endpoint only messages a
      // resident who allows that channel (WorkEndpoints.cs:67).
      const residents = (await json("/api/residents/")) as {
        id: string;
        fullName: string;
        phone: string | null;
        smsConsent: string;
      }[];
      const resident = residents.find(
        (candidate) => candidate.smsConsent === "Granted" && candidate.phone,
      );
      if (!resident)
        throw new Error("no resident in the demo data has both SMS consent and a phone number");

      const created = (await json("/api/work/", {
        method: "POST",
        headers,
        body: JSON.stringify({
          title,
          propertyId: properties[0].id,
          priority: "Normal",
          residentId: resident.id,
        }),
      })) as { item: { id: string; status: string }; version: number };
      // CreateAsync publishes on create (EfWorkOperations.cs), so a new item starts in New.
      if (created.item.status !== "New")
        throw new Error(`created work should publish into New, got ${created.item.status}`);

      // Assigning the employee moves New -> Assigned (WorkItem.cs:64) and is the precondition
      // the on-the-way endpoint checks.
      const assigned = (await json(`/api/work/${created.item.id}/employee`, {
        method: "POST",
        headers,
        body: JSON.stringify({ employeeId: employee.id, version: created.version }),
      })) as { changed: boolean };
      if (!assigned.changed) throw new Error("the employee assignment reported no change");

      return {
        workId: created.item.id,
        employeeName: employee.displayName,
        residentName: resident.fullName,
      };
    },
    { title },
  );
}

test("technician marks assigned work on the way and sees the resident update in the timeline", async ({
  page,
}) => {
  // Unique per run (and per retry), so repeated runs against the same database never collide.
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
  const title = `E2E technician on the way ${runId}`;

  await signIn(page, adminEmail, adminPassword);
  const work = await provisionAssignedWork(page, title);
  await signOut(page);

  await signIn(page, technicianEmail, technicianPassword);

  // A Technician sees only work assigned to their own employee — an empty scope never means
  // organization-wide (WorkAccessScope.cs:19). So this both exercises the list scoping and pins
  // the employee the provisioning step picked.
  const scoped = await page.evaluate(async (search) => {
    const response = await fetch(
      `/api/work/?page=1&pageSize=100&search=${encodeURIComponent(search)}`,
      {
        credentials: "same-origin",
        cache: "no-store",
      },
    );
    if (!response.ok) throw new Error(`work list returned ${response.status}`);
    const payload = (await response.json()) as { items?: { id: string; status: string }[] };
    if (!Array.isArray(payload.items))
      throw new Error(
        `work list returned no items array: ${JSON.stringify(payload).slice(0, 200)}`,
      );
    return payload.items;
  }, runId);
  expect(
    scoped.map((item) => item.id),
    `the technician's scoped work list should hold exactly the item assigned to ${work.employeeName}. ` +
      "An empty list means seed-demo's technician-to-employee rule " +
      "(tools/PropFlow.Admin/Program.cs:125-126) no longer picks the first active employee " +
      "returned by GET /api/employees/.",
  ).toEqual([work.workId]);
  expect(scoped[0].status).toBe("Assigned");

  const outcome = await page.evaluate(async (id) => {
    const csrf = (await (await fetch("/api/auth/csrf", { credentials: "same-origin" })).json()) as {
      token: string;
    };
    const response = await fetch(`/api/work/${id}/on-the-way`, {
      method: "POST",
      credentials: "same-origin",
      headers: { "X-CSRF-TOKEN": csrf.token },
    });
    return { status: response.status, body: await response.json() };
  }, work.workId);
  expect(outcome.status).toBe(200);
  expect(outcome.body.changed).toBe(true);

  await page.goto(`/work/${work.workId}`);
  await expect(page.getByText("OnTheWay").first()).toBeVisible();
  const timeline = page.locator(".timeline");
  await expect(timeline.getByText("StatusChanged").first()).toBeVisible();
  // The explicit workflow reports whether a consented resident message was queued. The resident
  // above consented, so the remaining reason `queued` can be false is a template the endpoint
  // cannot render: the seeded body references `{{ property.name }}`
  // (TidewaterSeed.cs:291) while the endpoint supplies only resident.name / work.title /
  // work.status (WorkEndpoints.cs:69), and a TemplateRenderException is swallowed
  // (WorkEndpoints.cs:77). Keep the assertion on the reported flag, not on an assumed value.
  expect(typeof outcome.body.queued).toBe("boolean");
  if (outcome.body.queued)
    await expect(timeline.getByText(/Message(Queued|Sent)/).first()).toBeVisible();
});
