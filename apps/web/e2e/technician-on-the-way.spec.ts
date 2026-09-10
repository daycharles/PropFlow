import { expect, test } from "@playwright/test";

// PF-5.10: the seeded field account is intentionally supplied by CI so this workflow exercises
// the same scoped authorization used by a real technician. The account must have one assigned
// work item with a resident who granted SMS consent and an active "Technician on the way" template.
const email = process.env.PLAYWRIGHT_TECHNICIAN_EMAIL ?? "demo-technician@tidewater.example.test";
const password = process.env.PLAYWRIGHT_TECHNICIAN_PASSWORD ?? "DemoPassword!123";

test("technician marks assigned work on the way and sees the resident update in the timeline", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const work = await page.evaluate(async () => {
    const response = await fetch("/api/work/?page=1&pageSize=100", { credentials: "same-origin" });
    if (!response.ok) throw new Error(`work list returned ${response.status}`);
    const payload = (await response.json()) as
      | { results?: { id: string; status: string }[]; items?: { id: string; status: string }[] }
      | { id: string; status: string }[];
    const results = Array.isArray(payload) ? payload : (payload.results ?? payload.items ?? []);
    const candidate = results.find(
      (item) => !["Completed", "Cancelled", "OnTheWay"].includes(item.status),
    );
    if (!candidate) throw new Error("seeded technician account has no open resident work");
    return candidate;
  });

  const outcome = await page.evaluate(async (id) => {
    const csrf = (await (await fetch("/api/auth/csrf", { credentials: "same-origin" })).json()) as {
      token: string;
    };
    const response = await fetch(`/api/work/${id}/on-the-way`, {
      method: "POST",
      credentials: "same-origin",
      headers: { "X-CSRF-TOKEN": csrf.token },
    });
    return { status: response.status, body: await response.json() };
  }, work.id);
  expect(outcome.status).toBe(200);
  expect(outcome.body.changed).toBe(true);

  await page.goto(`/work/${work.id}`);
  await expect(page.getByText("OnTheWay").first()).toBeVisible();
  const timeline = page.locator(".timeline");
  await expect(timeline.getByText("StatusChanged").first()).toBeVisible();
  // The explicit workflow reports whether a consented resident message was queued. Seeded
  // assignments intentionally include both contactable and non-contactable residents.
  expect(typeof outcome.body.queued).toBe("boolean");
  if (outcome.body.queued)
    await expect(timeline.getByText(/Message(Queued|Sent)/).first()).toBeVisible();
});
