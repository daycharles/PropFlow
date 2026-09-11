import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("property manager creates and activates a lease", async ({ page }) => {
  const residentName = `Browser Resident ${Date.now()}`;
  const targetName = `Transfer Resident ${Date.now()}`;
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByRole("link", { name: "Leases" }).click();
  const csrf = (await page.evaluate(async () => (await fetch("/api/auth/csrf")).json())) as {
    token: string;
  };
  const residentResponse = await page.evaluate(
    async ({ token, fullName }) =>
      fetch("/api/residents/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ fullName, email: `${Date.now()}@example.test`, phone: null }),
      }).then(async (response) => ({ status: response.status, body: await response.json() })),
    { token: csrf.token, fullName: residentName },
  );
  expect(residentResponse.status).toBe(201);
  const target = await page.evaluate(
    async ({ token, fullName }) => {
      const residentResponse = await fetch("/api/residents/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ fullName, email: `${Date.now()}@example.test`, phone: null }),
      });
      const resident = (await residentResponse.json()) as { id: string };
      const properties = (await fetch("/api/properties/?q=Harbor%20View").then((response) =>
        response.json(),
      )) as { id: string }[];
      const space = (await fetch(`/api/properties/${properties[0].id}/spaces`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ buildingId: null, code: `T-${Date.now()}` }),
      }).then((response) => response.json())) as { id: string };
      return { residentId: resident.id, spaceId: space.id };
    },
    { token: csrf.token, fullName: targetName },
  );
  await page.reload();
  await page.getByLabel("Lease resident").selectOption({ label: residentName });
  await page.getByLabel("Lease space").selectOption({ label: "101" });
  await page.getByRole("button", { name: "Create lease" }).click();
  await expect(page.getByText("Lease created.")).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: residentName }).last();
  await expect(row).toContainText("Draft");
  await row.getByRole("button", { name: "Activate" }).click();
  await expect(row).toContainText("Active");
  await row.getByRole("button", { name: "Parties" }).click();
  await page.getByLabel("Lease party name").fill("Browser Co-signer");
  await page.getByLabel("Lease party role").fill("Co-signer");
  await page.getByLabel("Lease party email").fill("cosigner@example.test");
  await page.getByRole("button", { name: "Add lease party" }).click();
  await expect(page.getByText("Lease party added.")).toBeVisible();
  await expect(page.getByText(/Browser Co-signer — Co-signer/)).toBeVisible();
  await row.getByRole("button", { name: "Charges" }).click();
  await page.getByLabel("Charge type").selectOption("Recurring");
  await page.getByLabel("Charge description").fill("Monthly rent");
  await page.getByLabel("Charge amount").fill("1700");
  await page.getByLabel("Charge due date").fill("2027-01-01");
  await page.getByRole("button", { name: "Add charge" }).click();
  await expect(page.getByText("Charge added.")).toBeVisible();
  await expect(page.getByText(/Monthly rent — \$1,700 — due 2027-01-01 — Open/)).toBeVisible();
  const lifecycleValues = [
    target.residentId,
    target.spaceId,
    "2027-01-01",
    "2028-09-30",
    "1700",
    "2027-01-01",
    "2028-08-31",
  ];
  page.on("dialog", (dialog) => dialog.accept(lifecycleValues.shift()));
  await row.getByRole("button", { name: "Transfer" }).click();
  const leaseRow = page.getByRole("row").filter({ hasText: targetName }).last();
  await expect(leaseRow).toContainText("Active");
  await leaseRow.getByRole("button", { name: "Renew" }).click();
  await expect(leaseRow).toContainText("Renewed");
  await leaseRow.getByRole("button", { name: "Give move-out notice" }).click();
  const noticeRow = page.getByRole("row").filter({ hasText: targetName }).last();
  await expect(noticeRow).toContainText("NoticeGiven");
  await noticeRow.getByRole("button", { name: "Notices" }).click();
  await expect(page.getByText(/MoveOut — due 2028-08-31 — Open/)).toBeVisible();
  await page
    .getByRole("row")
    .filter({ hasText: targetName })
    .last()
    .getByRole("button", { name: "Complete move-out" })
    .click();
  await expect(page.getByRole("row").filter({ hasText: targetName }).last()).toContainText("Ended");
});
