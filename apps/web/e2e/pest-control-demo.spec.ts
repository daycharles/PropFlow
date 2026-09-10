import { expect, test, type Locator, type Page } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-4.11: the headline M4 demo (docs/demo-script.md §3a) — filter to a category, select the
// queue, assign a vendor + a visit window + a resident message, confirm, and see it on the
// timeline — running end to end against the seeded Tidewater org in CI.

function workRows(page: Page): Locator {
  return page.getByRole("row").filter({ has: page.locator('a[href^="/work/"]') });
}

/**
 * Provision this run's own pest-control work under the seeded "Pest control" category, each with
 * a resident who has SMS consent, so the demo never consumes the seed's own New items. Requests
 * run inside the page — the `__Host-` cookies never attach to Playwright's plain-http context.
 */
async function provisionPestControl(page: Page, runId: string, count: number) {
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
      const categories = (await json("/api/categories/")) as { id: string; name: string }[];
      const pestControl = categories.find((category) => category.name === "Pest control");
      if (!pestControl) throw new Error("the demo organization has no Pest control category");

      const created: { id: string; title: string }[] = [];
      for (let index = 1; index <= count; index += 1) {
        const resident = (await json("/api/residents", {
          method: "POST",
          headers,
          body: JSON.stringify({
            fullName: `E2E Pest Resident ${runId} #${index}`,
            email: `e2e-pest-${runId}-${index}@residents.example.test`,
            phone: `+1555${(3_000_000 + index).toString()}`,
          }),
        })) as { id: string };
        await json(`/api/residents/${resident.id}/consent`, {
          method: "PUT",
          headers,
          body: JSON.stringify({ channel: "Sms", granted: true }),
        });
        const title = `E2E pest-control demo ${runId} #${index}`;
        const body = (await json("/api/work/", {
          method: "POST",
          headers,
          body: JSON.stringify({
            title,
            propertyId: properties[0].id,
            categoryId: pestControl.id,
            residentId: resident.id,
            priority: "High",
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

test("the bulk pest-control assign + schedule + notify demo runs end to end", async ({ page }) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const created = await provisionPestControl(page, runId, 6);
  const n = created.length;

  // The demo's filter sequence: category, then the morning New queue.
  await page.getByLabel("Category").selectOption({ label: "Pest control" });
  await page.getByLabel("Search").fill(runId);
  await page.getByLabel("Status").selectOption({ label: "New" });
  await expect.poll(() => workRows(page).count()).toBe(n);

  await page.getByLabel("Select all visible work").check();
  await page.getByRole("button", { name: "Assign & notify" }).click();

  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Assign & notify" })).toBeVisible();
  await expect(dialog).toContainText(`${n} work items selected`);

  const vendorPicker = dialog.getByLabel("Vendor");
  const vendorOptions = await vendorPicker
    .locator("option")
    .evaluateAll<
      { value: string; label: string }[],
      HTMLOptionElement
    >((options) => options.map((option) => ({ value: option.value, label: option.text })));
  const vendor =
    vendorOptions.find((option) => option.value && /pest/i.test(option.label)) ??
    vendorOptions.find((option) => option.value);
  expect(vendor, "the vendor picker offered a selectable vendor").toBeTruthy();
  await vendorPicker.selectOption(vendor!.value);

  await dialog.getByLabel("Visit start").fill("2026-12-01T08:00");
  await dialog.getByLabel("Visit end").fill("2026-12-01T11:00");

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
  expect(summary).not.toMatch(/\b(undefined|null|NaN|\[object Object\])\b/i);
  expect(summary).toMatch(new RegExp(`Assigned .+ to ${n} of ${n} work items`));
  expect(summary).toMatch(new RegExp(`Scheduled ${n} of ${n}`));
  expect(summary).toMatch(new RegExp(`Queued ${n} of ${n} resident messages`));

  await dialog.getByRole("button", { name: "Done" }).click();
  await expect(dialog).toBeHidden();

  // Every item moved out of New into Scheduled.
  await page.getByLabel("Status").selectOption({ value: "" });
  await expect(page.getByRole("columnheader").first()).toBeVisible();
  for (const item of created) {
    const row = page.getByRole("row").filter({ has: page.locator(`a[href="/work/${item.id}"]`) });
    await expect(row).toHaveCount(1);
    await expect(row.getByText("Scheduled")).toBeVisible();
  }

  // The audit trail: vendor, schedule and the resident message all on one item's timeline.
  await page
    .getByRole("row")
    .filter({ has: page.locator(`a[href="/work/${created[0].id}"]`) })
    .getByRole("link")
    .first()
    .click();
  await expect(page.getByRole("heading", { name: "Timeline" })).toBeVisible();
  const timeline = page.locator(".timeline");
  await expect(timeline.getByText("VendorAssigned").first()).toBeVisible();
  await expect(timeline.getByText("Scheduled").first()).toBeVisible();
  await expect(timeline.getByText(/Message(Queued|Sent)/).first()).toBeVisible();
});
