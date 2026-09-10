import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.07: the "Needs your attention" screen — Critical / Warning / Informational cards over the
// /api/attention feed, each filtering the list, every item linking to its work order.
test("the attention screen surfaces a critical item and filters by card", async ({ page }) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const title = `E2E attention ${runId} burst pipe`;
  const { workId } = await page.evaluate(
    async ({ title }) => {
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
      const property = ((await json("/api/properties/")) as { id: string }[])[0];
      const body = (await json("/api/work/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ title, propertyId: property.id, priority: "Critical" }),
      })) as { item: { id: string } };
      return { workId: body.item.id };
    },
    { title },
  );

  await page.getByRole("link", { name: "Needs attention" }).click();
  await expect(page.getByRole("heading", { name: "Needs your attention" })).toBeVisible();

  // The critical card counts our unassigned emergency.
  const criticalCard = page.getByRole("button", { name: /^\d+ Critical/ });
  await expect(criticalCard.locator(".attention-count")).not.toHaveText("0");

  // The item is listed with its reason and links to the work order.
  const row = page.locator(".attention-row", { hasText: title });
  await expect(row).toContainText("Unassigned emergency");
  await expect(row.getByRole("link")).toHaveAttribute("href", `/work/${workId}`);

  // Clicking the critical card filters the list to critical items.
  await criticalCard.click();
  await expect(criticalCard).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("heading", { name: /Critical items/ })).toBeVisible();
  await expect(page.locator(".attention-row", { hasText: title })).toBeVisible();

  // And through to the work order itself.
  await row.getByRole("link").click();
  await expect(page).toHaveURL(new RegExp(`/work/${workId}$`));
});
