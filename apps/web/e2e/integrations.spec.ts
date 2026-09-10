import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.11: the Integrations health screen — connected system, status, last successful sync,
// failure count, unresolved records — with a "Sync now" action.
test("the integrations screen shows connection health and runs a sync", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  // Ensure a mock connection exists (idempotent — a retry or a sibling run may have made it).
  await page.evaluate(async () => {
    const json = async (path: string, init?: RequestInit) => {
      const response = await fetch(path, {
        credentials: "same-origin",
        cache: "no-store",
        ...init,
      });
      return {
        ok: response.ok,
        status: response.status,
        body: await response.json().catch(() => null),
      };
    };
    const existing = (await json("/api/integrations")).body as { sourceSystem: string }[];
    if (existing.some((c) => c.sourceSystem === "mock")) return;
    const { body } = await json("/api/auth/csrf");
    await json("/api/integrations", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": (body as { token: string }).token,
      },
      body: JSON.stringify({ sourceSystem: "mock", displayName: "Mock property system" }),
    });
  });

  await page.getByRole("link", { name: "Integrations" }).click();
  await expect(page.getByRole("heading", { name: "Integrations" })).toBeVisible();

  const card = page.locator(".integration-card", { hasText: "mock" });
  await expect(card).toBeVisible();

  // Run a sync and confirm the connection reports healthy with tracked records.
  await card.getByRole("button", { name: "Sync now" }).click();
  await expect(card.getByText(/Sync completed:/)).toBeVisible();
  await expect(card.locator(".badge.status-good")).toHaveText("Healthy");

  const tracked = card.locator('dt:has-text("Tracked records") + dd');
  await expect(tracked).not.toHaveText("0");

  // The tracked records are browsable.
  await card.getByRole("button", { name: "View records" }).click();
  await expect(card.locator(".integration-record-list li").first()).toBeVisible();
});
