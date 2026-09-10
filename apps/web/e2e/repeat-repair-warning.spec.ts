import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.05: once an asset crosses the repeat-repair threshold (default 3 in 120 days) the warning
// shows the repair count, total repair cost and asset age — on the asset page and beside the
// work detail's asset picker.
test("the repeat-repair warning appears on the asset page and the work detail", async ({
  page,
}) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const { assetId, workId } = await page.evaluate(
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

      // Reset the policy to its defaults so the run is deterministic regardless of other specs.
      await json("/api/assets/repeat-repair-policy", {
        method: "PUT",
        headers,
        body: JSON.stringify({ repairThreshold: 3, windowDays: 120, matchByCategory: false }),
      });

      let workId = "";
      for (const [index, cost] of [140, 260, 175].entries()) {
        const body = (await json("/api/work/", {
          method: "POST",
          headers,
          body: JSON.stringify({
            title: `E2E repeat repair ${runId} #${index + 1}`,
            propertyId: property.id,
            assetId: asset.id,
            cost,
          }),
        })) as { item: { id: string } };
        workId = body.item.id;
      }
      return { assetId: asset.id, workId };
    },
    { runId },
  );

  // Asset page: the full-width warning banner.
  await page.goto(`/assets/${assetId}`);
  const banner = page.getByTestId("repeat-repair-warning");
  await expect(banner).toBeVisible();
  await expect(banner).toContainText("Repeat repair");
  await expect(banner).toContainText("Repair cost");
  await expect(banner).toContainText("Asset age");

  // Work detail: the compact warning next to the asset picker.
  await page.goto(`/work/${workId}`);
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  await expect(page.getByTestId("repeat-repair-warning")).toBeVisible();
});
