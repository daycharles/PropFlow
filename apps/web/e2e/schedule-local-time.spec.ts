import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

/**
 * Pin the browser to a fixed, non-UTC zone.
 *
 * This is load-bearing, not tidiness. At UTC+00:00 a UTC read and a local read are
 * indistinguishable, so the defect this spec guards is invisible on a UTC machine — which is
 * what the CI runner is. Fixing the zone makes the assertions below mean the same thing on the
 * runner, on a developer box, and on the box the bug was found on.
 *
 * America/New_York is UTC-4 on 2026-09-15 (EDT), so the stored instant's UTC digits (13:00)
 * differ from the wall clock the user typed (09:00) — the whole point.
 */
test.use({ timezoneId: "America/New_York" });

/** What the user types into, and must keep seeing in, `<input type="datetime-local">`. */
const startWallClock = "2026-09-15T09:00";
const endWallClock = "2026-09-15T12:00";

/**
 * The same two moments as absolute instants. Written with an explicit `Z` and parsed here rather
 * than in the browser, so these constants do not depend on the test process's own timezone:
 * 09:00 EDT is 13:00Z, 12:00 EDT is 16:00Z.
 */
const expectedStartMs = Date.parse("2026-09-15T13:00:00Z");
const expectedEndMs = Date.parse("2026-09-15T16:00:00Z");

/**
 * Read the schedule straight from the API, bypassing the input entirely, so the assertion is
 * about the stored instant and not about the same helper that renders it. GET /api/work/{id}
 * returns either a `{ item, version }` envelope or a flat item (lib/api.ts:310,321-326), so
 * accept both.
 *
 * The fetch runs *inside the page*: the session and antiforgery cookies are `__Host-` prefixed
 * and `Secure` (.claude/rules/web.md), so Playwright's APIRequestContext would be anonymous.
 */
async function storedSchedule(page: import("@playwright/test").Page, workId: string) {
  return page.evaluate(async (id) => {
    const response = await fetch(`/api/work/${id}`, {
      credentials: "same-origin",
      cache: "no-store",
    });
    if (!response.ok) throw new Error(`GET /api/work/${id} returned ${response.status}`);
    const body = (await response.json()) as {
      item?: { scheduledStart?: string | null; scheduledEnd?: string | null };
      scheduledStart?: string | null;
      scheduledEnd?: string | null;
    };
    const item = body.item ?? body;
    return {
      scheduledStart: item.scheduledStart ?? null,
      scheduledEnd: item.scheduledEnd ?? null,
    };
  }, workId);
}

/**
 * D2 regression. `dateTimeInput` (apps/web/app/work/[id]/page.tsx:574-581) used to format the
 * stored instant with `toISOString().slice(0, 16)`, which is UTC, into an input that reads and
 * writes LOCAL wall-clock time. A 9:00 AM visit window stored correctly as 13:00Z rendered back
 * as 1:00 PM, and because the onChange at :366/:379 parses the input as local and converts to
 * UTC, re-confirming the window the user was shown pushed the stored instant forward by the UTC
 * offset on every single pass.
 *
 * So this spec pins both halves: the wall clock survives a reload, and it survives being
 * re-saved from what the input displays.
 */
test("a scheduled window survives a reload and a re-save in local wall-clock time", async ({
  page,
}) => {
  const runId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  // Provision this spec's own work item rather than consuming the demo seed, so the suite stays
  // re-runnable against the same database (m3-workflow.spec.ts:32-40 explains why).
  const workId = await page.evaluate(
    async ({ runId }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok) throw new Error(`${init?.method ?? "GET"} ${path} → ${response.status}`);
        return response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const properties = (await json("/api/properties/")) as { id: string }[];
      if (properties.length === 0) throw new Error("the demo organization has no property");
      const created = (await json("/api/work/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({
          title: `E2E schedule local time ${runId}`,
          propertyId: properties[0].id,
          priority: "Normal",
        }),
      })) as { item: { id: string } };
      // Scheduling is refused outright unless the item already has a vendor or employee
      // (WorkItem.Schedule, src/PropFlow.Domain/Work/WorkItem.cs:67), so assign one here.
      // Without this the Confirm click surfaces "Scheduling requires a vendor or employee."
      // and no schedule is ever stored, so the round trip under test never happens.
      const vendors = (await json("/api/vendors/")) as { id: string }[];
      if (vendors.length === 0) throw new Error("the demo organization has no vendor");
      await json(`/api/work/${created.item.id}/vendor`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ vendorId: vendors[0].id }),
      });
      return created.item.id;
    },
    { runId },
  );

  await page.goto(`/work/${workId}`);
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();

  const start = page.getByLabel("Start", { exact: true });
  const end = page.getByLabel("End", { exact: true });
  await expect(start).toHaveValue("");

  await start.fill(startWallClock);
  await end.fill(endWallClock);
  await page.getByRole("button", { name: "Confirm schedule…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByText("Saved")).toBeVisible();

  // The write path is the half that was already correct: the input is local, storage is UTC.
  // Assert it explicitly, so nobody can "fix" a future failure by making both sides UTC.
  const afterFirstSave = await storedSchedule(page, workId);
  expect(
    new Date(afterFirstSave.scheduledStart ?? "").getTime(),
    `${startWallClock} in America/New_York should store as 2026-09-15T13:00Z, got ${afterFirstSave.scheduledStart}`,
  ).toBe(expectedStartMs);
  expect(new Date(afterFirstSave.scheduledEnd ?? "").getTime()).toBe(expectedEndMs);

  // The regression itself: reload and the input must show the wall clock that was typed, not the
  // stored instant's UTC digits ("2026-09-15T13:00").
  await page.reload();
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  await expect(start).toHaveValue(startWallClock);
  await expect(end).toHaveValue(endWallClock);

  // Now the corrupting action, exactly as a user performs it: re-confirm the window the page is
  // displaying. Re-filling each input from its own displayed value fires the onChange that
  // parses it as local, so a UTC-formatted read would be re-interpreted as local and shift the
  // stored instant by the offset. With read and write as inverses, the instant must not move.
  await start.fill(await start.inputValue());
  await end.fill(await end.inputValue());
  await page.getByRole("button", { name: "Confirm schedule…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByText("Saved")).toBeVisible();

  const afterSecondSave = await storedSchedule(page, workId);
  expect(
    new Date(afterSecondSave.scheduledStart ?? "").getTime(),
    `re-saving the displayed window shifted the stored start: ${afterFirstSave.scheduledStart} → ${afterSecondSave.scheduledStart}`,
  ).toBe(expectedStartMs);
  expect(
    new Date(afterSecondSave.scheduledEnd ?? "").getTime(),
    `re-saving the displayed window shifted the stored end: ${afterFirstSave.scheduledEnd} → ${afterSecondSave.scheduledEnd}`,
  ).toBe(expectedEndMs);

  // And it still reads back as the same wall clock after the second round trip.
  await page.reload();
  await expect(page.getByRole("heading", { name: "Details" })).toBeVisible();
  await expect(start).toHaveValue(startWallClock);
  await expect(end).toHaveValue(endWallClock);
});
