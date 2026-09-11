import { expect, test } from "@playwright/test";

test("the login screen offers account recovery without account enumeration", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Forgot password?" }).click();
  await expect(page.getByRole("button", { name: "Send reset instructions" })).toBeVisible();
  await page.getByLabel("Organization slug").first().fill("tidewater-demo");
  await page.getByLabel("Email").first().fill("unknown@example.test");
  await page.getByRole("button", { name: "Send reset instructions" }).click();
  await expect(page.locator("p.message")).toHaveText(
    "If the account exists, reset instructions have been sent.",
  );
  await expect(page.getByRole("button", { name: "Reset password" })).toBeVisible();
});
