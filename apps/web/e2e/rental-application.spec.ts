import { expect, test } from "@playwright/test";

/**
 * PF-S05.09 — the corrected applicant flow on /marketing/listings, end to end.
 *
 * What this guards is the thing the old panel got wrong: it had one click from "applicant" to
 * "Approved", with no consent and no decision row. So the assertions that matter here are the
 * negative ones — "Send to screening" is DISABLED while the application is only Submitted, and
 * the reason is on screen — not merely that the happy path reaches Approved.
 *
 * The verdict is not asserted. ConfiguredScreeningProvider derives Pass/Review/Fail from a stable
 * hash of the idempotency key (Screening/ConfiguredScreeningProvider.cs), so which one this
 * application draws depends on ids generated at run time. The spec therefore fills BOTH the
 * reason and the note before approving, which is valid for every verdict — and is exactly what a
 * Fail would require. Forcing a verdict means setting `Screening:ProviderMode`, which is a stack
 * configuration change and belongs to the integration suite, not to a browser spec.
 *
 * The provider-outage path (503, nothing written, rendered as a retryable transient) is likewise
 * not reachable from a browser without `Screening:ProviderMode=Unavailable` on the API process.
 * It is covered by the panel's own 503 branch and by PF-S05.06's tests.
 */

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("an applicant reaches Approved only through consent, screening and a recorded decision", async ({
  page,
}) => {
  const stamp = Date.now();
  const headline = `Application Listing ${stamp}`;
  const prospect = `Robin Applicant ${stamp}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByRole("link", { name: "Listings" }).click();

  // A published listing with one inquiry, converted to an applicant — the state the panel starts
  // from. Everything above this line is PF-3 behaviour and is only scaffolding here.
  await page.getByLabel("Listing property").selectOption({ label: "Harbor View Apartments" });
  await page.getByLabel("Listing headline").fill(headline);
  await page.getByLabel("Monthly rent").fill("2100");
  await page.getByRole("button", { name: "Create listing" }).click();
  await expect(page.getByText("Listing created.")).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: headline });
  await row.getByRole("button", { name: "Publish" }).click();
  await expect(row).toContainText("Published");
  await row.getByRole("button", { name: "Activity" }).click();
  await page.getByLabel("Prospect name").fill(prospect);
  await page.getByLabel("Prospect email").fill(`robin-${stamp}@example.test`);
  await page.getByRole("button", { name: "Record inquiry" }).click();
  await expect(page.getByText("Inquiry recorded.")).toBeVisible();
  await page.getByRole("button", { name: "Convert to applicant" }).click();
  await expect(page.getByText(new RegExp(`${prospect}.*New`))).toBeVisible();

  // 1. Create the application, put the person on it as Primary, submit.
  await page.getByRole("button", { name: "Start application" }).click();
  await expect(page.getByText("Application started.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Application — Submitted" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Primary", exact: true })).toBeVisible();

  // 2. The consent gate, asserted as a gate: the control is unavailable and says why. An enabled
  //    button that answers 409 would satisfy the API and fail this.
  const sendToScreening = page.getByRole("button", { name: "Send to screening" });
  await expect(sendToScreening).toBeDisabled();
  await expect(
    page.getByText("Every applicant must record consent before screening can start."),
  ).toBeVisible();
  await expect(page.getByRole("cell", { name: "No consent recorded" })).toBeVisible();

  // 3. Record consent. The application advances to ConsentGranted and the gate opens.
  await page.getByLabel("Consent type").selectOption("BackgroundCheck");
  await page.getByLabel("Consent decision").selectOption("Granted");
  await page.getByRole("button", { name: "Record consent" }).click();
  await expect(page.getByRole("heading", { name: "Application — ConsentGranted" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "BackgroundCheck" })).toBeVisible();
  await expect(sendToScreening).toBeEnabled();

  // 4. Screening runs, and the application lands in UnderReview with a screening row.
  await sendToScreening.click();
  await expect(page.getByRole("heading", { name: "Application — UnderReview" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Completed" })).toBeVisible();

  // 5. Decide. The note is filled unconditionally because a Fail verdict would require it as an
  //    override, and the panel disables Approve until it is there.
  const reason = `Income verified ${stamp}`;
  await page.getByLabel("Decision reason").fill(reason);
  await page
    .locator('textarea[aria-label="Override note"], textarea[aria-label="Decision note"]')
    .fill("Individualized assessment completed; references and income confirmed.");
  await page.getByRole("button", { name: "Approve application" }).click();

  // 6. The decision is recorded and visible in the append-only trail.
  await expect(page.getByRole("heading", { name: "Application — Approved" })).toBeVisible();
  const decision = page.getByRole("row").filter({ hasText: reason });
  await expect(decision).toContainText("Approved");
  await expect(page.getByText("No decisions recorded yet.")).toBeHidden();
});
