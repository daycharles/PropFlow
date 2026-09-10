import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// The card layout hides the sortable column headers on a phone, so the Work list keeps a
// dedicated Sort control at that width. This drives the real app rather than a stubbed DOM.
test.use({ viewport: { width: 375, height: 812 } });

test("the phone Work list exposes a working Sort control and does not scroll sideways", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  // No sideways scroll on the list.
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    ),
  ).toBeLessThanOrEqual(1);

  // The header row is visually collapsed (1px clip) on a phone, so the Sort control stands in.
  const theadBox = await page.locator("table thead").boundingBox();
  expect(theadBox?.height ?? 999, "the column-header row is collapsed on a phone").toBeLessThan(5);
  const sort = page.getByLabel("Sort by");
  await expect(sort).toBeVisible();

  // Choosing a sort option reorders the cards, and the control keeps the chosen value.
  const titles = () =>
    page.locator('tbody tr:has(a[href^="/work/"]) a[href^="/work/"]').allInnerTexts();
  await expect.poll(async () => (await titles()).length).toBeGreaterThan(1);
  const byTitleAsc = (await titles()).join("|");

  await sort.selectOption("title:desc");
  await expect(sort).toHaveValue("title:desc");
  await expect.poll(async () => (await titles()).join("|")).not.toBe(byTitleAsc);

  await sort.selectOption("dueDate:asc");
  await expect(sort).toHaveValue("dueDate:asc");
  await expect.poll(async () => (await titles()).join("|")).not.toBe(byTitleAsc);
});
