import { expect, test, type Page } from "@playwright/test";

/**
 * PF-S19.09 — the FS-S19 acceptance narrative through the browser:
 * connect → sync → review conflicts → fix the mapping → promote → sync → reconciled.
 *
 * It runs against `isolation-demo`, NOT the `tidewater-demo` org every other spec uses. There can
 * be only one connection per source system per organization (POST /api/integrations answers 409
 * on a duplicate), so a second spec driving the `mock` connection in tidewater would be fighting
 * `integrations.spec.ts` for the same row — over sync claims, over the mapping mode, over the
 * conflict queue. `isolation-demo` is seeded with its own admin, portfolio, property and space
 * (tools/PropFlow.Admin/Program.cs:105,110) and no other spec touches it.
 *
 * Both tests are RE-RUNNABLE against a database that already has this connection: each starts by
 * forcing the Property profile back to a cleared, ReportOnly state, so the refusal is reached the
 * same way on the first run and the fiftieth. That is also why nothing asserts `added > 0` — a
 * second run reconciles the same unchanged records and correctly reports zero added.
 *
 * The narrative is split in two because half of it is currently unreachable: see the `test.fixme`
 * below and its explanation. The mapping half runs.
 */

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "isolation-demo";
const email = "demo-admin@isolation.example.test";

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();
}

async function ensureMockConnection(page: Page) {
  await page.evaluate(async () => {
    const json = async (path: string, init?: RequestInit) => {
      const response = await fetch(path, {
        credentials: "same-origin",
        cache: "no-store",
        ...init,
      });
      return {
        ok: response.ok,
        status: response.status,
        body: await response.json().catch(() => null),
      };
    };
    const existing = (await json("/api/integrations")).body as { sourceSystem: string }[];
    if (existing.some((connection) => connection.sourceSystem === "mock")) return;
    const { body } = await json("/api/auth/csrf");
    await json("/api/integrations", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-TOKEN": (body as { token: string }).token,
      },
      body: JSON.stringify({ sourceSystem: "mock", displayName: "Mock property system" }),
    });
  });
}

test("the mapping panel refuses a promotion with its reasons, then promotes once they are fixed", async ({
  page,
}) => {
  await signIn(page);
  await ensureMockConnection(page);
  await page.getByRole("link", { name: "Integrations" }).click();
  await expect(page.getByRole("heading", { name: "Integrations" })).toBeVisible();

  const card = page.locator(".integration-card", { hasText: "mock" });
  await expect(card).toBeVisible();
  const panel = card.locator(".integration-subpanel");

  // ---- 1. Start from a cleared, ReportOnly Property profile. Clearing a required default demotes
  //         an AutoApply profile on its own (MappingProfile.SetDefaults → DemoteWhenInvalid), which
  //         is the behaviour relied on here rather than a test-only reset hook.
  await card.getByRole("button", { name: /^Mapping$/ }).click();
  await expect(panel.getByRole("heading", { name: "Mapping" })).toBeVisible();
  await panel.getByLabel("Target portfolio").selectOption("");
  await panel.getByLabel("Default time zone").fill("");
  await panel.getByRole("button", { name: /mapping$/ }).click();

  // ReportOnly has to be unmistakable: not the word, the consequence.
  await expect(panel.getByTestId("mapping-mode")).toContainText("ReportOnly");
  await expect(panel.getByTestId("mapping-mode")).toContainText(
    "a sync raises conflicts and writes nothing into PropFlow",
  );

  // ---- 2. The blocking issues are on screen before anything is attempted, and the informational
  //         ones are kept separate from them.
  const errors = panel.getByTestId("mapping-errors");
  await expect(errors).toContainText("TargetPortfolioId");
  await expect(errors).toContainText("DefaultTimeZoneId");

  // ---- 3. The guard rail refuses the promotion AND says why. A 409 rendered as "conflict" would
  //         waste the whole mechanism, so the issue list is what is asserted.
  await panel.getByRole("button", { name: "Promote to auto-apply" }).click();
  const refusal = panel.getByTestId("promotion-refusal");
  await expect(refusal).toBeVisible();
  await expect(refusal).toContainText("TargetPortfolioId");
  await expect(refusal).toContainText("DefaultTimeZoneId");

  // ---- 4. Fix exactly what the refusal named.
  await panel.getByLabel("Target portfolio").selectOption({ label: "Isolation Portfolio" });
  await panel.getByLabel("Default time zone").fill("America/New_York");
  await panel.getByRole("button", { name: /mapping$/ }).click();
  await expect(panel.getByText("Mapping defaults saved.")).toBeVisible();
  await expect(panel.getByTestId("mapping-errors")).toBeHidden();

  // ---- 5. Promote. AutoApply must be as unmistakable as ReportOnly was.
  await panel.getByRole("button", { name: "Promote to auto-apply" }).click();
  await expect(panel.getByTestId("mapping-mode")).toContainText("AutoApply");
  await expect(panel.getByTestId("mapping-mode")).toContainText(
    "a sync writes these records into PropFlow",
  );

  // ---- 6. Sync, and read the health back off the run history: counts, trigger and status are what
  //         make a connection raising the same divergence every run distinguishable from a clean one.
  await card.getByRole("button", { name: "Sync now" }).click();
  await expect(card.getByText(/Sync completed:/)).toBeVisible();
  await card.getByRole("button", { name: /^History$/ }).click();
  await expect(panel.getByRole("heading", { name: "Sync history" })).toBeVisible();
  const latestRun = panel.getByRole("row").filter({ hasText: "Manual" }).first();
  await expect(latestRun).toBeVisible();
  await expect(latestRun).toContainText("Completed");
});

/**
 * BLOCKED ON A BACKEND DEFECT, NOT ON THIS UI. Marked fixme rather than deleted because the
 * coverage is the point and the gap should be visible in the suite, not only in a report.
 *
 * `EfIntegrationReconciler`'s `Pass.Conflict(...)` builds a `Conflict`, caches it in
 * `OpenConflicts` and appends it to `raised` — but nothing ever adds a NEW conflict to
 * `IntegrationStore.Conflicts`, and `Pass.Raised` (EfIntegrationReconciler.cs:563) is the only
 * reference to that list in the entire repository. A conflict re-observed from a row already in
 * the database persists, because that row is tracked; a first observation is dropped on the floor.
 *
 * Observed against a real stack on 2026-09-12 (mock adapter, isolation-demo):
 *   POST /api/integrations/{id}/sync  → {"outcome":"Completed","seen":11,"conflicted":6,...}
 *   GET  /api/integrations/{id}/conflicts → {"items":[],"totalCount":0,"openCount":0,"staleCount":0}
 *   GET  /api/integrations/{id}        → "openConflicts":0
 *   SELECT count(*) FROM integrations."Conflicts"  → 0   (as the table owner, so not RLS)
 * The six links are correctly marked SyncState=Conflicted with their lastError, so the reconciler
 * decided right and only the persistence is missing.
 *
 * Until a first-observation conflict is persisted there is no row for the queue to show, no
 * `seenInLatestRun` to go false and no `staleCount` to move, so every assertion below is about
 * behaviour the product cannot currently exhibit. Un-fixme this with the reconciler fix.
 */
test.fixme(
  "the conflict queue separates what the latest run still sees from what it no longer does",
  async ({ page }) => {
    await signIn(page);
    await ensureMockConnection(page);
    await page.getByRole("link", { name: "Integrations" }).click();

    const card = page.locator(".integration-card", { hasText: "mock" });
    const panel = card.locator(".integration-subpanel");

    // Cleared + ReportOnly, so the first sync of this run raises the mapping conflicts.
    await card.getByRole("button", { name: /^Mapping$/ }).click();
    await panel.getByLabel("Target portfolio").selectOption("");
    await panel.getByLabel("Default time zone").fill("");
    await panel.getByRole("button", { name: /mapping$/ }).click();
    await expect(panel.getByTestId("mapping-mode")).toContainText("ReportOnly");

    // A first sync against an unmapped connection raises conflicts and is NOT a failure: the
    // connection stays healthy and the open-conflict badge is what moves.
    await card.getByRole("button", { name: "Sync now" }).click();
    await expect(card.getByText(/Sync completed:/)).toBeVisible();
    await expect(card.getByTestId("open-conflicts")).not.toHaveText("0");

    // The queue opens on Open conflicts the latest run still sees.
    await card.getByRole("button", { name: /^Conflicts/ }).click();
    await expect(panel.getByLabel("Conflict status")).toHaveValue("Open");
    await expect(panel.getByLabel("Conflict freshness")).toHaveValue("current");
    const portfolioConflict = panel
      .locator(".conflict-row")
      .filter({ hasText: "TargetPortfolioId" })
      .first();
    await expect(portfolioConflict).toBeVisible();
    await expect(portfolioConflict).toContainText("MissingRequiredMapping");

    // Fix the mapping and promote.
    await card.getByRole("button", { name: /^Mapping$/ }).click();
    await panel.getByLabel("Target portfolio").selectOption({ label: "Isolation Portfolio" });
    await panel.getByLabel("Default time zone").fill("America/New_York");
    await panel.getByRole("button", { name: /mapping$/ }).click();
    await panel.getByRole("button", { name: "Promote to auto-apply" }).click();
    await expect(panel.getByTestId("mapping-mode")).toContainText("AutoApply");

    await card.getByRole("button", { name: "Sync now" }).click();
    await expect(card.getByText(/Sync completed:/)).toBeVisible();

    // The honest ending, and the assertion the whole staleness design exists for. The reconciler
    // does not CLOSE the conflict it fixed — it stops re-observing it — so the row is still Open
    // and it is the default view that has to change.
    await card.getByRole("button", { name: /^Conflicts/ }).click();
    // The explainer renders only when staleCount > 0, so its presence IS the assertion. A
    // substring check on the counts line would not do: "10 no longer seen…" contains "0 no
    // longer seen…".
    await expect(panel.getByTestId("stale-explainer")).toBeVisible();
    await expect(
      panel.locator(".conflict-row").filter({ hasText: "TargetPortfolioId" }),
    ).toHaveCount(0);

    await panel.getByLabel("Conflict freshness").selectOption("stale");
    const stale = panel.locator(".conflict-row").filter({ hasText: "TargetPortfolioId" }).first();
    await expect(stale).toBeVisible();
    await expect(stale).toContainText("Not seen in the latest run");
    // Still Open: only a person closes a conflict, and resolvedByUserId keeps its meaning because
    // no synthetic system actor was invented to auto-close it.
    await expect(stale).toContainText("Open");
  },
);
