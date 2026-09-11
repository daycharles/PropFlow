import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("resident can submit and see a service request", async ({ page }) => {
  const title = `Browser portal request ${Date.now()}`;
  const announcementTitle = `Browser announcement ${Date.now()}`;
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
  const csrf = (await page.evaluate(async () => (await fetch("/api/auth/csrf")).json())) as {
    token: string;
  };
  const demoLease = await page.evaluate(
    async ({ token }) => {
      const residents = (await fetch("/api/residents/?q=demo-resident").then((response) =>
        response.json(),
      )) as { id: string; email?: string | null }[];
      const resident =
        residents.find((item) => item.email === "demo-resident@tidewater.example.test") ??
        residents[0];
      const properties = (await fetch("/api/properties/?q=Harbor%20View").then((response) =>
        response.json(),
      )) as { id: string }[];
      const space = (await fetch(`/api/properties/${properties[0].id}/spaces`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ buildingId: null, code: `PAY-${Date.now()}` }),
      }).then((response) => response.json())) as { id: string };
      const leaseResponse = await fetch("/api/leasing/leases/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({
          residentId: resident.id,
          spaceId: space.id,
          startsOn: "2026-01-01",
          endsOn: "2026-12-31",
          monthlyRent: 1650,
          securityDeposit: null,
        }),
      });
      const lease = (await leaseResponse.json()) as { id: string };
      const chargeResponse = await fetch(`/api/leasing/leases/${lease.id}/charges`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({
          type: "Recurring",
          description: "Browser monthly rent",
          amount: 1650,
          dueOn: "2026-04-01",
        }),
      });
      const charge = (await chargeResponse.json()) as { id: string };
      return { ...lease, chargeId: charge.id };
    },
    { token: csrf.token },
  );
  expect(demoLease.id).toBeTruthy();
  await page.getByRole("link", { name: "Announcements" }).click();
  await page.getByLabel("Announcement title").fill(announcementTitle);
  await page.getByLabel("Announcement body").fill("Office hours change this week.");
  await page.getByRole("button", { name: "Create announcement" }).click();
  await expect(page.getByText("Announcement created.")).toBeVisible();
  const announcementRow = page.getByRole("row").filter({ hasText: announcementTitle });
  await announcementRow.getByRole("button", { name: "Publish" }).click();
  await expect(announcementRow).toContainText("Published");
  await page.getByRole("button", { name: "Sign out" }).click();
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-resident@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("link", { name: "Resident portal" })).toBeVisible();
  await page.getByRole("link", { name: "Resident portal" }).click();
  await expect(page.getByRole("heading", { name: "Resident portal" })).toBeVisible();
  await expect(page.getByRole("heading", { name: announcementTitle })).toBeVisible();
  await page.getByLabel("Payment lease charge").selectOption(demoLease.chargeId);
  await expect(page.getByLabel("Payment amount")).toHaveValue("1650");
  await expect(page.getByLabel("Payment due date")).toHaveValue("2026-04-01");
  await page.getByLabel("Payment amount").fill("1650");
  await page.getByLabel("Payment due date").fill("2026-04-01");
  await page.getByLabel("Payment reference").fill(`Browser rent ${Date.now()}`);
  await page.getByRole("button", { name: "Submit payment" }).click();
  await expect(page.getByText("Payment submitted.")).toBeVisible();
  await page.getByLabel("Service request title").fill(title);
  await page.getByLabel("Service request details").fill("The kitchen faucet is dripping.");
  await page.getByRole("button", { name: "Submit request" }).click();
  await expect(page.getByText("Service request submitted.")).toBeVisible();
  await expect(page.getByRole("cell", { name: title })).toBeVisible();
  await page.getByLabel("Profile phone").fill("+15555550123");
  await page.getByRole("button", { name: "Save profile" }).click();
  await expect(page.getByText("Profile updated.")).toBeVisible();
  await page.getByLabel("Email notifications").check();
  await page.getByRole("button", { name: "Save preferences" }).click();
  await expect(page.getByText("Communication preferences updated.")).toBeVisible();
  await page.getByLabel("Household member name").fill("Browser Household Member");
  await page.getByLabel("Household member relationship").fill("Partner");
  await page.getByLabel("Household member email").fill("household@example.test");
  await page.getByRole("button", { name: "Add household member" }).click();
  await expect(page.getByText("Household member added.")).toBeVisible();
  await expect(page.getByText(/Browser Household Member — Partner/).first()).toBeVisible();
});
