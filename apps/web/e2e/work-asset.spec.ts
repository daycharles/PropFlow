import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.02: the work detail workspace links a work item to one of its property's assets, and the
// change lands on the timeline.
test("the work detail page links an asset and records it on the timeline", async ({ page }) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const { workId, assetName } = await page.evaluate(
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
      const assets = (await json(`/api/assets/?propertyId=${property.id}`)) as {
        id: string;
        name: string;
      }[];
      if (assets.length === 0) throw new Error("the demo property has no assets");
      const created = (await json("/api/work/", {
        method: "POST",
        headers,
        body: JSON.stringify({ title: `E2E asset link ${runId}`, propertyId: property.id }),
      })) as { item: { id: string } };
      return { workId: created.item.id, assetName: assets[0].name };
    },
    { runId },
  );

  await page.goto(`/work/${workId}`);
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();

  const assetPicker = page.getByLabel("Asset");
  await expect(assetPicker).toHaveValue("");
  await assetPicker.selectOption({ label: assetName });
  await page.getByRole("button", { name: /Save details/ }).click();
  await expect(page.getByText("Saved")).toBeVisible();

  await expect(page.locator(".timeline").getByText("AssetLinked")).toBeVisible();
  // The link survives a reload.
  await page.reload();
  await expect(page.getByLabel("Asset")).toHaveValue(/.+/);
});
