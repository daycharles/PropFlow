import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.03: the asset detail page shows the asset's complete maintenance history and the cost /
// count roll-ups.
test("the asset page lists every linked work item and totals its costs", async ({ page }) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const { assetId } = await page.evaluate(
    async ({ runId }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok) throw new Error(`${init?.method ?? "GET"} ${path} → ${response.status}`);
        return response.status === 204 ? null : response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const headers = { "Content-Type": "application/json", "X-CSRF-TOKEN": token };
      const property = ((await json("/api/properties/")) as { id: string }[])[0];
      const asset = ((await json(`/api/assets/?propertyId=${property.id}`)) as { id: string }[])[0];

      for (const [index, cost] of [125, 300].entries()) {
        await json("/api/work/", {
          method: "POST",
          headers,
          body: JSON.stringify({
            title: `E2E asset history ${runId} #${index + 1}`,
            propertyId: property.id,
            assetId: asset.id,
            cost,
          }),
        });
      }
      return { assetId: asset.id };
    },
    { runId },
  );

  await page.goto(`/assets/${assetId}`);
  await expect(page.getByRole("heading", { name: "Maintenance history" })).toBeVisible();

  // Both of this run's work items appear, and clicking one reaches its work detail.
  const rows = page.locator(`tbody tr:has(a[href^="/work/"])`).filter({ hasText: runId });
  await expect(rows).toHaveCount(2);

  // The roll-ups reflect at least this run's two costed work orders.
  const summary = (await page.locator(".detail-grid").innerText()).replace(/\s+/g, " ");
  const count = Number(summary.match(/Work orders\s+(\d+)/)?.[1] ?? "0");
  expect(count).toBeGreaterThanOrEqual(2);
  expect(summary).toMatch(/Total logged cost\s+\$[\d,]+\.\d\d/);

  await rows.first().getByRole("link").first().click();
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  expect(page.url()).toContain("/work/");
});
