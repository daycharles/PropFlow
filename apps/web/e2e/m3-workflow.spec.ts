import { expect, test, type Locator, type Page } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

/**
 * Resolve a work-table column by its header text rather than by a hardcoded index, so adding or
 * re-ordering a column cannot silently move an assertion onto the wrong cell. Header cells and
 * body cells are 1:1, so the returned index is valid for `getByRole("cell")` inside a row.
 */
async function columnIndex(page: Page, label: string): Promise<number> {
  await expect(page.getByRole("columnheader").first()).toBeVisible();
  const headers = await page.getByRole("columnheader").allInnerTexts();
  const index = headers.findIndex((text) =>
    text.trim().toLowerCase().startsWith(label.toLowerCase()),
  );
  expect(index, `expected a "${label}" column, saw ${JSON.stringify(headers)}`).toBeGreaterThan(-1);
  return index;
}

/** Rows that represent a work item — excludes the header row and the loading/empty placeholders. */
function workRows(page: Page): Locator {
  return page.getByRole("row").filter({ has: page.locator('a[href^="/work/"]') });
}

/** A single row, identified by the work id in its detail link rather than by title or position. */
function rowFor(page: Page, id: string): Locator {
  return page.getByRole("row").filter({ has: page.locator(`a[href="/work/${id}"]`) });
}

/**
 * Provision the work this test will act on, so the run does not consume the demo seed.
 *
 * Assigning a vendor moves a New item to Assigned (src/PropFlow.Domain/Work/WorkItem.cs:52) and
 * `seed-demo` skips organizations that already exist, so a spec that ate the seed's New items
 * could only ever pass once against a given database — precisely the wrong property for a suite
 * someone re-runs after a failure.
 *
 * The requests run *inside the page* via `page.evaluate`, not through `page.request`. The session
 * and antiforgery cookies are `__Host-` prefixed and `Secure` (src/PropFlow.Api/Program.cs:79-81,
 * 93-95); Playwright's APIRequestContext will not attach a Secure cookie to the plain-http
 * baseURL, so `page.request` is anonymous and every call comes back 401. Fetching from the page
 * uses the browser's own cookie jar, which is also exactly what apps/web/lib/api.ts does.
 *
 * Mutations need the `X-CSRF-TOKEN` header from GET /api/auth/csrf (SessionEndpoints.cs:12),
 * which every non-GET request under /api is validated against (src/PropFlow.Api/Program.cs:140).
 */
async function createWorkItems(page: Page, runId: string, count: number) {
  const outcome = await page.evaluate(
    async ({ runId, count }) => {
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
      const properties = (await json("/api/properties/")) as { id: string; name: string }[];
      if (properties.length === 0) throw new Error("the demo organization has no property");

      const created: { id: string; title: string; status: string }[] = [];
      for (let index = 1; index <= count; index += 1) {
        const title = `E2E vendor column ${runId} #${index}`;
        const body = (await json("/api/work/", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
          body: JSON.stringify({ title, propertyId: properties[0].id, priority: "Normal" }),
        })) as { item: { id: string; status: string } };
        created.push({ id: body.item.id, title, status: body.item.status });
      }
      return created;
    },
    { runId, count },
  );

  // CreateAsync publishes on create (EfWorkOperations.cs:68), which is the precondition for the
  // Status=New filter below.
  for (const item of outcome)
    expect(item.status, `${item.title} should be published into New`).toBe("New");
  return outcome;
}

test("demo administrator bulk-assigns a vendor and sees the vendor and counts", async ({
  page,
}) => {
  // Unique per run (and per retry), so repeated runs against the same database never collide.
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const created = await createWorkItems(page, runId, 3);

  // Scope the list to this run's work. The search filter is an ILIKE over title and description
  // (EfWorkOperations.cs:17), so the run id isolates these rows exactly. Status=New is kept as
  // well: WorkItem.AssignVendor has no terminal-status guard, so a select-all must never be able
  // to reach Cancelled or Completed work.
  await page.getByLabel("Search").fill(runId);
  await page.getByLabel("Status").selectOption({ label: "New" });

  const rows = workRows(page);
  await expect
    .poll(() => rows.count(), { message: `work items created for run ${runId}` })
    .toBe(created.length);

  const statusColumn = await columnIndex(page, "Status");
  for (const item of created) {
    const row = rowFor(page, item.id);
    await expect(row, `expected exactly one row for ${item.title}`).toHaveCount(1);
    await expect(row.getByRole("cell").nth(statusColumn)).toHaveText("New");
  }

  await page.getByLabel("Select all visible work").check();
  await page.getByRole("button", { name: "Assign vendor" }).click();

  const dialog = page.getByRole("dialog");
  const vendorPicker = dialog.getByLabel("Vendor");
  const vendorOptions = await vendorPicker
    .locator("option")
    .evaluateAll<
      { value: string; label: string }[],
      HTMLOptionElement
    >((options) => options.map((option) => ({ value: option.value, label: option.text.trim() })));
  const vendor = vendorOptions.find((option) => option.value !== "" && option.label !== "");
  expect(
    vendor,
    `the vendor picker offered nothing selectable: ${JSON.stringify(vendorOptions)}`,
  ).toBeTruthy();
  const vendorName = (vendor as { value: string; label: string }).label;

  await vendorPicker.selectOption((vendor as { value: string; label: string }).value);
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("button", { name: "Confirm assignment" }).click();

  await expect(dialog.getByRole("heading", { name: "Vendor assigned" })).toBeVisible();

  // The summary must report the counts POST /api/work/bulk/vendor returned, not a boolean flag.
  // Every selected item was created by this run and had no vendor, so all of them changed.
  const summary = (await dialog.innerText()).replace(/\s+/g, " ").trim();
  const counts = summary.match(/\d+/g) ?? [];
  expect(counts.length, `success summary reported no number: "${summary}"`).toBeGreaterThan(0);
  expect(summary).not.toMatch(/\b(true|false|undefined|null|NaN)\b/i);
  expect(summary).not.toContain("[object Object]");
  expect(
    Number(counts[0]),
    `expected ${created.length} assignments to be reported in "${summary}"`,
  ).toBe(created.length);
  expect(summary).toContain(vendorName);

  await page.getByRole("button", { name: "Done" }).click();

  // Assigning a vendor moves a New item to Assigned (WorkItem.cs:52), so these rows fall out of
  // the Status=New filter. Drop the status filter but keep the run-id search: the same rows come
  // back, now carrying a vendor.
  await page.getByLabel("Status").selectOption({ value: "" });

  // The regression this spec exists for: the Vendor column showed "Unassigned" after a successful
  // assignment because the list response carried no vendor name.
  const vendorColumn = await columnIndex(page, "Vendor");
  for (const item of created) {
    const row = rowFor(page, item.id);
    await expect(row, `expected exactly one row for ${item.title}`).toHaveCount(1);
    await expect(
      row.getByRole("cell").nth(vendorColumn),
      `Vendor column for ${item.title} after assignment`,
    ).toHaveText(vendorName);
  }

  await rowFor(page, created[0].id).getByRole("link").first().click();
  await expect(page.getByRole("heading", { name: "Timeline" })).toBeVisible();
  await expect(page.getByText("VendorAssigned").first()).toBeVisible();
});
