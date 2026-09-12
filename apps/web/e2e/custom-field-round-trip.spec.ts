import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-S03.09. A custom field defined through the real Settings UI (PF-S03.08) is then set on a
// work item and read back - through the API, since there is no UI yet for setting a value on a
// work item (docs/api.md: customFields on work create/update/detail is deliberately API-only).
// This ties the definition (PF-S03.01), the value (PF-S03.02), and the admin UI (PF-S03.08)
// together in one spec, the way schedule-local-time.spec.ts does for the schedule fields.
test("a custom field defined in Settings is set on a work item and round-trips through the API", async ({
  page,
}) => {
  // Lowercase alphanumeric only, matching CustomFieldDefinition's key format
  // (`^[a-z][a-z0-9_]*$`) - a dash from Date.now().toString(36) would 400.
  const runId = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`.replace(
    /[^a-z0-9]/g,
    "",
  );
  const key = `f_${runId}`;
  const label = `E2E field ${runId}`;
  const value = `value-${runId}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  // Define the field through the real Settings UI, not by seeding it - PF-S03.08's own point.
  await page.goto("/settings/configuration");
  await expect(page.getByRole("heading", { name: "Custom fields" })).toBeVisible();
  await page.getByLabel("Key").fill(key);
  await page.getByLabel("Label").fill(label);
  await page.getByRole("button", { name: "Add field" }).click();
  await expect(page.getByText(label)).toBeVisible();

  // Set it on a new work item and read it back. Runs inside the page via fetch, not
  // page.request: the session/antiforgery cookies are `__Host-` and Secure
  // (.claude/rules/web.md), so an out-of-page request context would be anonymous.
  const result = await page.evaluate(
    async ({ key, value }) => {
      const json = async (path: string, init?: RequestInit) => {
        const response = await fetch(path, {
          credentials: "same-origin",
          cache: "no-store",
          ...init,
        });
        if (!response.ok)
          throw new Error(
            `${init?.method ?? "GET"} ${path} returned ${response.status}: ${await response.text()}`,
          );
        return response.json();
      };
      const { token } = (await json("/api/auth/csrf")) as { token: string };
      const headers = { "Content-Type": "application/json", "X-CSRF-TOKEN": token };
      const properties = (await json("/api/properties/")) as { id: string }[];
      if (properties.length === 0) throw new Error("the demo organization has no property");

      const created = (await json("/api/work/", {
        method: "POST",
        headers,
        body: JSON.stringify({
          title: `E2E custom field round trip ${key}`,
          propertyId: properties[0].id,
          customFields: { [key]: value },
        }),
      })) as { item: { id: string }; customFields?: Record<string, string> };

      const detail = (await json(`/api/work/${created.item.id}`)) as {
        customFields?: Record<string, string>;
      };

      return { onCreate: created.customFields?.[key], onDetail: detail.customFields?.[key] };
    },
    { key, value },
  );

  expect(result.onCreate).toBe(value);
  expect(result.onDetail).toBe(value);
});
