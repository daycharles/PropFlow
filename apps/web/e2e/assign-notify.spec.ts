import { expect, test, type Locator, type Page } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

/** Rows that represent a work item — excludes the header row and the loading/empty placeholders. */
function workRows(page: Page): Locator {
  return page.getByRole("row").filter({ has: page.locator('a[href^="/work/"]') });
}

/**
 * Provision this run's own residents and work so the spec never eats the demo seed. Each work
 * item is published (New) with a resident who has SMS consent, so the "notify" step has someone
 * to message. Requests run inside the page for the same reason as m3-workflow.spec.ts: the
 * `__Host-` cookies never attach to Playwright's plain-http APIRequestContext.
 */
async function provision(page: Page, runId: string, count: number) {
  return page.evaluate(
    async ({ runId, count }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok)
          throw new Error(`${init?.method ?? "GET"} ${path} returned ${response.status}`);
        return response.status === 204 ? null : response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const headers = { "Content-Type": "application/json", "X-CSRF-TOKEN": token };
      const properties = (await json("/api/properties/")) as { id: string }[];
      if (properties.length === 0) throw new Error("the demo organization has no property");

      const created: { id: string; title: string }[] = [];
      for (let index = 1; index <= count; index += 1) {
        const resident = (await json("/api/residents", {
          method: "POST",
          headers,
          body: JSON.stringify({
            fullName: `E2E Resident ${runId} #${index}`,
            email: `e2e-${runId}-${index}@residents.example.test`,
            phone: `+1555${(2_000_000 + index).toString()}`,
          }),
        })) as { id: string };
        await json(`/api/residents/${resident.id}/consent`, {
          method: "PUT",
          headers,
          body: JSON.stringify({ channel: "Sms", granted: true }),
        });
        const title = `E2E assign-notify ${runId} #${index}`;
        const body = (await json("/api/work/", {
          method: "POST",
          headers,
          body: JSON.stringify({
            title,
            propertyId: properties[0].id,
            residentId: resident.id,
            priority: "Normal",
          }),
        })) as { item: { id: string; status: string } };
        if (body.item.status !== "New") throw new Error(`${title} was not published into New`);
        created.push({ id: body.item.id, title });
      }
      return created;
    },
    { runId, count },
  );
}

test("demo administrator assigns a vendor, schedules a window, and notifies residents at once", async ({
  page,
}) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const created = await provision(page, runId, 3);

  await page.getByLabel("Search").fill(runId);
  await page.getByLabel("Status").selectOption({ label: "New" });
  await expect.poll(() => workRows(page).count()).toBe(created.length);

  await page.getByLabel("Select all visible work").check();
  await page.getByRole("button", { name: "Assign & notify" }).click();

  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Assign & notify" })).toBeVisible();

  const vendorPicker = dialog.getByLabel("Vendor");
  const vendorValue = await vendorPicker
    .locator("option")
    .nth(1)
    .evaluate((option: HTMLOptionElement) => option.value);
  await vendorPicker.selectOption(vendorValue);

  await dialog.getByLabel("Visit start").fill("2026-11-02T09:00");
  await dialog.getByLabel("Visit end").fill("2026-11-02T12:00");

  await dialog.getByLabel("Send a message to residents about this visit").check();
  const template = dialog.getByLabel("Template");
  const smsTemplate = await template
    .locator("option")
    .filter({ hasText: "Visit scheduled (SMS)" })
    .first()
    .evaluate((option: HTMLOptionElement) => option.value);
  await template.selectOption(smsTemplate);

  await dialog.getByRole("button", { name: "Continue" }).click();
  await expect(dialog.getByRole("heading", { name: "Confirm" })).toBeVisible();
  await dialog.getByRole("button", { name: "Confirm" }).click();

  await expect(dialog.getByRole("heading", { name: "Done" })).toBeVisible({ timeout: 15000 });
  const summary = (await dialog.innerText()).replace(/\s+/g, " ").trim();
  const n = created.length;
  expect(summary).not.toMatch(/\b(undefined|null|NaN|\[object Object\])\b/i);
  expect(summary).toMatch(new RegExp(`Assigned .+ to ${n} of ${n} work items`));
  expect(summary).toMatch(new RegExp(`Scheduled ${n} of ${n}`));
  expect(summary).toMatch(new RegExp(`Queued ${n} of ${n} resident messages`));

  await dialog.getByRole("button", { name: "Done" }).click();
  await expect(dialog).toBeHidden();

  // The items left New for Scheduled; drop the status filter and confirm the vendor stuck.
  await page.getByLabel("Status").selectOption({ value: "" });
  const scheduledColumn = page.getByRole("columnheader");
  await expect(scheduledColumn.first()).toBeVisible();
  for (const item of created) {
    const row = page.getByRole("row").filter({ has: page.locator(`a[href="/work/${item.id}"]`) });
    await expect(row).toHaveCount(1);
    await expect(row.getByText("Scheduled")).toBeVisible();
  }

  // The resident message is on the work timeline.
  await page
    .getByRole("row")
    .filter({ has: page.locator(`a[href="/work/${created[0].id}"]`) })
    .getByRole("link")
    .first()
    .click();
  await expect(page.getByRole("heading", { name: "Timeline" })).toBeVisible();
  await expect(page.getByText(/Message(Queued|Sent)/).first()).toBeVisible();
});
