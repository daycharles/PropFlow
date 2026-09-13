import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("authenticated admin can reach communications and document workflow APIs", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const campaigns = await page.request.get("/api/communication/campaigns");
  const templates = await page.request.get("/api/communication/document-templates");
  expect(campaigns.ok()).toBeTruthy();
  expect(templates.ok()).toBeTruthy();
});

test("workflow APIs reject unauthenticated browser requests", async ({ request }) => {
  expect((await request.get("/api/communication/conversations")).status()).toBe(401);
  expect((await request.get("/api/communication/document-packets")).status()).toBe(401);
});
