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
 * Every test here is RE-RUNNABLE against a database that already has this connection: each starts
 * by forcing the Property profile into the state it needs, so the refusal is reached the same way
 * on the first run and the fiftieth. That is also why nothing asserts `added > 0` — a second run
 * reconciles the same unchanged records and correctly reports zero added.
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
 * The half of the narrative the conflict queue exists for.
 *
 * A first sync of the mock snapshot against a cleared Property profile raises SIX conflicts, not
 * the four a reading of the mapping defaults suggests: two properties (TargetPortfolioId), two
 * work orders (DefaultCreatorId) and two ASSETS. The assets are the non-obvious pair —
 * `ReconciliationRules.Decide` evaluates the record's own mapping gap BEFORE parent state, so
 * `MOCK-ASSET-1`/`-2` conflict on their non-blank `Kind` even though the property they hang off
 * never reconciled. Asserting a count here rather than just "more than zero" is what would catch
 * that ordering silently changing.
 */
test("the conflict queue separates what the latest run still sees from what it no longer does", async ({
  page,
}) => {
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
  await expect(card.getByTestId("open-conflicts")).toHaveText("6");

  // The queue opens on Open conflicts the latest run still sees.
  await card.getByRole("button", { name: /^Conflicts/ }).click();
  await expect(panel.getByLabel("Conflict status")).toHaveValue("Open");
  await expect(panel.getByLabel("Conflict freshness")).toHaveValue("current");
  await expect(panel.locator(".conflict-row")).toHaveCount(6);
  await expect(panel.getByTestId("conflict-counts")).toContainText("6 open");
  await expect(panel.getByTestId("conflict-counts")).toContainText("showing 6 of 6");
  // Nothing has gone stale yet, so the explainer must be absent rather than saying "0".
  await expect(panel.getByTestId("stale-explainer")).toBeHidden();

  const portfolioConflict = panel
    .locator(".conflict-row")
    .filter({ hasText: "TargetPortfolioId" })
    .first();
  await expect(portfolioConflict).toBeVisible();
  await expect(portfolioConflict).toContainText("MissingRequiredMapping");
  // The two assets, which conflict on their own Kind before their parent property is ever
  // considered. If Decide's ordering changed, this is the assertion that would notice.
  await expect(panel.locator(".conflict-row").filter({ hasText: "UnmappedValue" })).toHaveCount(2);

  // A kind filter goes to the server, and the connection-wide open count must NOT move with it.
  await panel.getByLabel("Conflict kind").selectOption("Property");
  await expect(panel.locator(".conflict-row")).toHaveCount(2);
  await expect(panel.getByTestId("conflict-counts")).toContainText("6 open");
  await expect(panel.getByTestId("conflict-counts")).toContainText("showing 2 of 2");
  await panel.getByLabel("Conflict kind").selectOption("");

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
  // and it is the default view that has to change. openConflicts stays 6 for exactly that
  // reason: two of the six went stale, none of them were closed.
  await expect(card.getByTestId("open-conflicts")).toHaveText("6");
  await card.getByRole("button", { name: /^Conflicts/ }).click();
  // The explainer renders only when staleCount > 0, so its presence IS the assertion. A
  // substring check on the counts line would not do: "10 no longer seen…" contains "0 no
  // longer seen…".
  await expect(panel.getByTestId("stale-explainer")).toBeVisible();
  await expect(panel.getByTestId("conflict-counts")).toContainText("6 open");
  await expect(panel.getByTestId("conflict-counts")).toContainText("showing 4 of 6");
  await expect(panel.locator(".conflict-row").filter({ hasText: "TargetPortfolioId" })).toHaveCount(
    0,
  );

  await panel.getByLabel("Conflict freshness").selectOption("stale");
  await expect(panel.locator(".conflict-row")).toHaveCount(2);
  const stale = panel.locator(".conflict-row").filter({ hasText: "TargetPortfolioId" }).first();
  await expect(stale).toBeVisible();
  await expect(stale).toContainText("Not seen in the latest run");
  // Still Open: only a person closes a conflict, and resolvedByUserId keeps its meaning because
  // no synthetic system actor was invented to auto-close it.
  await expect(stale).toContainText("Open");

  // Resolving is that person's decision. It is a status transition, never a delete — the runtime
  // role holds no DELETE grant on integrations."Conflicts" to make one with.
  await stale.getByRole("button", { name: "Resolve" }).click();
  await expect(panel.locator(".conflict-row")).toHaveCount(1);
  await expect(card.getByTestId("open-conflicts")).toHaveText("5");

  // The row is still there under Resolved. A second resolve answers 409, and the UI reaches that
  // state by construction rather than by asking: Resolve/Ignore render only while the conflict is
  // Open, so a closed one has no button to press twice.
  await panel.getByLabel("Conflict status").selectOption("Resolved");
  await panel.getByLabel("Conflict freshness").selectOption("all");
  const resolved = panel.locator(".conflict-row").filter({ hasText: "TargetPortfolioId" }).first();
  await expect(resolved).toContainText("Resolved");
  await expect(resolved.getByRole("button", { name: "Resolve" })).toHaveCount(0);
  await expect(resolved.getByRole("button", { name: "Ignore" })).toHaveCount(0);
});

/**
 * Retiring a link is operator-invoked and is NOT a delete of the PropFlow row the link reconciled
 * into. That distinction is the whole point of the feature, so the test leaves the browser to go
 * and look at the property afterwards rather than trusting the label.
 *
 * It retires a Synced link rather than an upstream disappearance. The deterministic mock returns
 * the same eleven records every pull, so it never stops reporting one and the UpstreamDisappearance
 * path is not reachable from a browser at all. `ExternalRecordLink.Observe` also sets SyncState
 * back to Synced, so the next sync un-retires this link and the test stays re-runnable.
 */
test("retiring a record link leaves the PropFlow row it created alone", async ({ page }) => {
  await signIn(page);
  await ensureMockConnection(page);
  await page.getByRole("link", { name: "Integrations" }).click();

  const card = page.locator(".integration-card", { hasText: "mock" });
  const panel = card.locator(".integration-subpanel");

  // Property has to be auto-applying for there to be a PropFlow row to leave alone.
  await card.getByRole("button", { name: /^Mapping$/ }).click();
  await panel.getByLabel("Target portfolio").selectOption({ label: "Isolation Portfolio" });
  await panel.getByLabel("Default time zone").fill("America/New_York");
  await panel.getByRole("button", { name: /mapping$/ }).click();
  const promote = panel.getByRole("button", { name: "Promote to auto-apply" });
  if (await promote.isVisible()) await promote.click();
  await expect(panel.getByTestId("mapping-mode")).toContainText("AutoApply");

  await card.getByRole("button", { name: "Sync now" }).click();
  await expect(card.getByText(/Sync completed:/)).toBeVisible();

  await card.getByRole("button", { name: "View records" }).click();
  const row = card.locator(".integration-record-list li", { hasText: "MOCK-PROP-2" });
  await expect(row).toContainText("Synced");
  await row.getByRole("button", { name: "Retire link" }).click();
  await expect(row).toContainText("Retired");
  await expect(row).toContainText("the PropFlow row it created is untouched");

  // And it really is untouched: the imported property is still listed.
  await page.getByRole("link", { name: "Properties" }).click();
  await expect(page.getByText("Birch Terrace").first()).toBeVisible();
});
