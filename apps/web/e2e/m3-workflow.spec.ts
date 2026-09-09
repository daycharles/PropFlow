import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("demo administrator bulk-assigns vendor and sees assignment history", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Leaking kitchen sink" })).toBeVisible();
  await page.getByLabel("Select all visible work").check();
  await page.getByRole("button", { name: "Assign vendor" }).click();
  await page
    .getByRole("dialog")
    .getByLabel("Vendor")
    .selectOption({ label: "Tidewater Pest Services" });
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("button", { name: "Confirm assignment" }).click();
  await expect(
    page.getByRole("dialog").getByRole("heading", { name: "Vendor assigned" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Done" }).click();

  await page.getByRole("link", { name: "Leaking kitchen sink" }).click();
  await expect(page.getByRole("heading", { name: "Timeline" })).toBeVisible();
  await expect(page.getByText("VendorAssigned")).toBeVisible();
});
