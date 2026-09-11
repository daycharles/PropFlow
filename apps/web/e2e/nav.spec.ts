import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.12: the primary nav is built from lib/navigation.ts and shows only finished, usable
// destinations. An admin sees the full set; detail pages never appear.
test("the primary navigation lists exactly the shipped destinations", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const nav = page.getByRole("navigation", { name: "Primary navigation" });
  const links = nav.getByRole("link");
  await expect(links).toHaveText([
    "Work",
    "Properties",
    "Listings",
    "Leases",
    "Announcements",
    "Needs attention",
    "Categories",
    "Automation",
    "Integrations",
    "Members",
  ]);
  await expect(nav.getByRole("link", { name: "Work" })).toHaveAttribute("href", "/");
  await expect(nav.getByRole("link", { name: "Properties" })).toHaveAttribute(
    "href",
    "/properties",
  );
  await expect(nav.getByRole("link", { name: "Needs attention" })).toHaveAttribute(
    "href",
    "/attention",
  );
  await expect(nav.getByRole("link", { name: "Categories" })).toHaveAttribute(
    "href",
    "/settings/categories",
  );
  await expect(nav.getByRole("link", { name: "Automation" })).toHaveAttribute(
    "href",
    "/settings/automation",
  );
  await expect(nav.getByRole("link", { name: "Integrations" })).toHaveAttribute(
    "href",
    "/integrations",
  );
  await expect(nav.getByRole("link", { name: "Members" })).toHaveAttribute(
    "href",
    "/settings/members",
  );

  // No detail routes leak into the nav, and the search control is present for a Work.Read user.
  await expect(nav.locator('a[href^="/work/"], a[href^="/assets/"]')).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Search/ })).toBeVisible();

  // The current page is marked on its nav entry.
  await nav.getByRole("link", { name: "Integrations" }).click();
  await expect(page.getByRole("heading", { name: "Integrations" })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Integrations" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});
