import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.13: the repeat HVAC repair walkthrough — the M6 story end to end. An HVAC asset takes
// three repairs inside the detection window; the asset page warns, the work detail warns, and
// the attention queue flags the open work.
test("a repeat HVAC repair surfaces on the asset, the work order and the attention queue", async ({
  page,
}) => {
  const runId = `${Math.random().toString(36).slice(2, 9)}`;
  const assetName = `Rooftop HVAC ${runId}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const { assetId, firstWorkId, workTitles } = await page.evaluate(
    async ({ assetName, runId }) => {
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

      // Detection uses the org default (3 repairs / 120 days).
      await json("/api/assets/repeat-repair-policy", {
        method: "PUT",
        headers,
        body: JSON.stringify({ repairThreshold: 3, windowDays: 120, matchByCategory: false }),
      });

      const property = ((await json("/api/properties/")) as { id: string }[])[0];
      const asset = (await json("/api/assets", {
        method: "POST",
        headers,
        body: JSON.stringify({
          kind: "Hvac",
          name: assetName,
          propertyId: property.id,
          spaceId: null,
          condition: "Fair",
          installedOn: "2018-04-01",
          expectedServiceLifeYears: 15,
        }),
      })) as { id: string };

      const workTitles = [
        `HVAC repair ${runId} — compressor`,
        `HVAC repair ${runId} — capacitor`,
        `HVAC repair ${runId} — blower motor`,
      ];
      const workIds: string[] = [];
      for (const [index, title] of workTitles.entries()) {
        const body = (await json("/api/work/", {
          method: "POST",
          headers,
          body: JSON.stringify({
            title,
            propertyId: property.id,
            assetId: asset.id,
            cost: 180 + index * 40,
          }),
        })) as { item: { id: string } };
        workIds.push(body.item.id);
      }
      return { assetId: asset.id, firstWorkId: workIds[0], workTitles };
    },
    { assetName, runId },
  );

  // 1. The asset page warns, and its maintenance history has all three repairs.
  await page.goto(`/assets/${assetId}`);
  await expect(page.getByRole("heading", { name: assetName })).toBeVisible();

  const warning = page.getByTestId("repeat-repair-warning");
  await expect(warning).toBeVisible();
  await expect(warning).toContainText("Repeat repair");
  await expect(warning.locator("dd").first()).toHaveText("3");

  const summary = (await page.locator(".detail-grid").innerText()).replace(/\s+/g, " ");
  expect(Number(summary.match(/Work orders\s+(\d+)/)?.[1] ?? "0")).toBeGreaterThanOrEqual(3);

  const historyRows = page.locator('tbody tr:has(a[href^="/work/"])').filter({ hasText: runId });
  await expect(historyRows).toHaveCount(3);

  // 2. Opening one of the repairs shows the compact warning beside the asset link.
  await page.goto(`/work/${firstWorkId}`);
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  await expect(page.getByTestId("repeat-repair-warning")).toBeVisible();

  // 3. The attention queue flags the open work as a repeat repair.
  await page.getByRole("link", { name: "Needs attention" }).click();
  await expect(page.getByRole("heading", { name: "Needs your attention" })).toBeVisible();

  const attentionRow = page
    .locator(".attention-row", { hasText: workTitles[0] })
    .filter({ hasText: "Repeat repair" });
  await expect(attentionRow).toBeVisible();
  await expect(attentionRow.getByRole("link")).toHaveAttribute("href", `/work/${firstWorkId}`);
});
