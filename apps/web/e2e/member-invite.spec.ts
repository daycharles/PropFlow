import { expect, test } from "@playwright/test";

// PF-S01.09: end-to-end coverage for the invite -> accept -> login flow, driving the real
// browser rather than the API directly (that path is already covered by
// IdentityAdministrationTests.Invite_create_list_and_accept_flow_creates_a_working_login).
const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const adminEmail = "demo-admin@tidewater.example.test";

// Unique per run so repeated local runs (and parallel CI shards) never collide on the
// Invitations.Email uniqueness constraint.
const inviteEmail = `e2e-invite-${Date.now()}@tidewater.example.test`;
const newPassword = "BrandNewPassw0rd!";

test("an invited member accepts their invitation and signs in", async ({ page }) => {
  // Sign in as the seeded Property Manager, who has Identity.ManageMembers.
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(adminEmail);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  await page
    .getByRole("navigation", { name: "Primary navigation" })
    .getByRole("link", { name: "Members" })
    .click();
  await expect(page.getByRole("heading", { name: "Members", exact: true })).toBeVisible();

  // Send the invitation as a Property Manager, so the invited user lands with
  // Identity.ManageMembers and the Members link appears in their own nav below.
  await page.getByLabel("Email").fill(inviteEmail);
  // Scoped by container, not accessible name: Chromium folds the select's own current value into
  // its computed label text, so "Role" alone matches inconsistently against the per-row "Role for
  // <email>" selects further down the page.
  await page.locator(".form-grid").getByLabel("Role").selectOption("Property Manager");
  await page.getByRole("button", { name: "Send invitation" }).click();

  const confirmation = page.getByRole("status");
  await expect(confirmation).toContainText(inviteEmail);
  const inviteLink = await confirmation.locator("code").textContent();
  expect(inviteLink).toBeTruthy();

  // Also shows up in the pending list immediately. exact:true because the confirmation banner
  // just above also contains this address as part of its own sentence.
  await expect(page.getByText(inviteEmail, { exact: true })).toBeVisible();

  // The invited person never sees the admin's session - a fresh, unauthenticated context
  // mirrors that isolation instead of reusing `page`'s cookies.
  const inviteeContext = await page.context().browser()!.newContext();
  const inviteePage = await inviteeContext.newPage();
  await inviteePage.goto(inviteLink!);

  await expect(inviteePage.getByRole("heading", { name: "Accept your invitation" })).toBeVisible();
  await inviteePage.getByLabel("Password", { exact: true }).fill(newPassword);
  await inviteePage.getByLabel("Confirm password").fill(newPassword);
  await inviteePage.getByRole("button", { name: "Accept invitation" }).click();

  await expect(inviteePage.getByRole("heading", { name: "You're all set" })).toBeVisible();
  await inviteePage.getByRole("link", { name: "Go to sign in" }).click();

  await inviteePage.getByLabel("Organization slug").fill(organizationSlug);
  await inviteePage.getByLabel("Email").fill(inviteEmail);
  await inviteePage.getByLabel("Password").fill(newPassword);
  await inviteePage.getByRole("button", { name: "Sign in" }).click();

  await expect(inviteePage.getByRole("heading", { name: "Work" })).toBeVisible();
  await expect(
    inviteePage
      .getByRole("navigation", { name: "Primary navigation" })
      .getByRole("link", { name: "Members" }),
  ).toBeVisible();

  await inviteeContext.close();

  // Back on the admin's page, the invitee has moved from pending to active: exactly one match
  // for their email (the active-members row) rather than a strict-mode violation from also still
  // appearing in the pending list confirms the move. Not asserting the pending list is empty
  // outright - other invitations from other runs/tests may legitimately still be pending.
  await page.reload();
  await expect(page.getByText(inviteEmail, { exact: true })).toBeVisible();
});
