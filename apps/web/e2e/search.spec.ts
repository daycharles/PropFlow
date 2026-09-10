import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

// PF-6.09: keyboard-first global search. Ctrl/Cmd+K opens the palette anywhere; typing queries
// /api/search; Enter opens the highlighted result, Escape closes.
test("the command palette opens on a shortcut and navigates to a work order", async ({ page }) => {
  // A distinctive token nothing else in the demo data (or a sibling spec's run) contains, so the
  // work order is the only hit and is unambiguously first.
  const token = `zzqx${Math.random().toString(36).slice(2, 12)}`;
  const title = `${token} palette target`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();

  const { workId } = await page.evaluate(
    async ({ title }) => {
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
      const property = ((await json("/api/properties/")) as { id: string }[])[0];
      const body = (await json("/api/work/", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ title, propertyId: property.id, priority: "Normal" }),
      })) as { item: { id: string } };
      return { workId: body.item.id };
    },
    { title },
  );

  // Open with the keyboard shortcut, from no particular focus.
  await page.locator("body").click();
  await page.keyboard.press("Control+k");
  const palette = page.getByRole("dialog", { name: "Search PropFlow" });
  await expect(palette).toBeVisible();

  await page.getByPlaceholder("Search work, assets, people, places…").fill(token);

  // The first option is highlighted by default (the keyboard story), and the exact-substring
  // match on the title makes it our work order.
  const first = palette.getByRole("option").first();
  await expect(first).toHaveAttribute("data-active", "true");
  await expect(first).toContainText(token);
  await expect(first).toContainText("Work");

  // Enter opens the highlighted result.
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(new RegExp(`/work/${workId}$`));

  // Reopen and check Escape closes it.
  await page.locator("body").click();
  await page.keyboard.press("Control+k");
  await expect(palette).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(palette).toBeHidden();
});
