import { expect, test, type Page } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();
}

async function createWork(page: Page, runId: string, count: number) {
  return page.evaluate(
    async ({ runId, count }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok) throw new Error(`${init?.method ?? "GET"} ${path} → ${response.status}`);
        return response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const property = ((await json("/api/properties/")) as { id: string }[])[0];
      const ids: string[] = [];
      for (let i = 1; i <= count; i += 1) {
        const body = (await json("/api/work/", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
          body: JSON.stringify({ title: `Bulk edit ${runId} #${i}`, propertyId: property.id }),
        })) as { item: { id: string } };
        ids.push(body.item.id);
      }
      return ids;
    },
    { runId, count },
  );
}

// PF-5.12: the remaining bulk actions (status / priority / schedule / note / reopen) on the
// work-list toolbar, exercised on desktop (status) and a phone viewport (note).
test("bulk status change from the toolbar on desktop", async ({ page }) => {
  const runId = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;
  await signIn(page);
  const ids = await createWork(page, runId, 2);

  await page.getByLabel("Search").fill(runId);
  const rows = page.getByRole("row").filter({ has: page.locator('a[href^="/work/"]') });
  await expect(rows).toHaveCount(2);

  await page.getByLabel("Select all visible work").check();
  await page.getByRole("button", { name: "Bulk edit…" }).click();

  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Action").selectOption("status");
  await dialog.getByLabel("New status").selectOption("OnHold");
  await dialog.getByRole("button", { name: "Continue" }).click();
  await dialog.getByRole("button", { name: "Confirm change status" }).click();

  await expect(dialog.getByText(/Applied to 2 of 2/)).toBeVisible();
  await dialog.getByRole("button", { name: "Done" }).click();

  // The list refreshed and both rows now read OnHold.
  await expect(rows.first().getByText("OnHold")).toBeVisible();
  await expect(rows.nth(1).getByText("OnHold")).toBeVisible();

  // The change is on the work item's timeline.
  await page.goto(`/work/${ids[0]}`);
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  await expect(page.locator(".timeline").getByText("StatusChanged")).toBeVisible();
});

test("bulk internal note from the toolbar on a phone viewport", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const runId = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;
  await signIn(page);
  const ids = await createWork(page, runId, 2);

  await page.getByLabel("Search").fill(runId);
  const rows = page.getByRole("row").filter({ has: page.locator('a[href^="/work/"]') });
  await expect(rows).toHaveCount(2);

  // The phone card layout hides the header "select all" checkbox (clipped thead); select each
  // row's own checkbox instead.
  await page.getByLabel(`Select Bulk edit ${runId} #1`).check();
  await page.getByLabel(`Select Bulk edit ${runId} #2`).check();
  await page.getByRole("button", { name: "Bulk edit…" }).click();

  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Action").selectOption("note");
  // getByLabel("Note") also matches the Action <select> (its "Add a note" option text), so scope
  // to the textbox role.
  await dialog.getByRole("textbox", { name: "Note" }).fill(`Sprayed on ${runId}`);
  await dialog.getByRole("button", { name: "Continue" }).click();
  await dialog.getByRole("button", { name: "Confirm add a note" }).click();

  await expect(dialog.getByText(/Applied to 2 of 2/)).toBeVisible();
  await dialog.getByRole("button", { name: "Done" }).click();

  await page.goto(`/work/${ids[0]}`);
  await expect(page.locator(".timeline").getByText("WorkNote")).toBeVisible();
});
