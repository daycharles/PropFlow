import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-4.09: the Work list and detail have to be usable one-handed in the field.
test.use({ viewport: { width: 375, height: 812 } });

test("the work list and detail fit a phone with large touch targets", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  // Nothing scrolls the page sideways.
  const listOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(listOverflow, "work list has no horizontal overflow").toBeLessThanOrEqual(1);

  // The wide table collapses: column headers are hidden and a mobile Sort control takes over.
  await expect(page.getByRole("columnheader", { name: "Title" })).toBeHidden();
  await expect(page.getByLabel("Sort by")).toBeVisible();

  // Filter controls are a comfortable tap target.
  const statusBox = await page.getByLabel("Status").boundingBox();
  expect(statusBox?.height ?? 0, "Status filter is >= 44px tall").toBeGreaterThanOrEqual(44);

  // Each work row is a tappable card linking to its detail page.
  const firstCard = page.locator('tbody tr:has(a[href^="/work/"])').first();
  await expect(firstCard).toBeVisible();
  await firstCard.getByRole("link").first().click();

  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  const detailOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(detailOverflow, "work detail has no horizontal overflow").toBeLessThanOrEqual(1);

  const saveBox = await page.getByRole("button", { name: /Save details/ }).boundingBox();
  expect(saveBox?.height ?? 0, "Save details button is >= 44px tall").toBeGreaterThanOrEqual(44);
});
