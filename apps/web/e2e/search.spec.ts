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

  // Open with the keyboard shortcut, from no particular focus: after the sign-in button
  // unmounts, focus falls back to the body, and Ctrl+K is handled on `window` regardless
  // (command-search.tsx:70).
  //
  // Park the pointer in the top-left corner first, and leave it there for the rest of the test.
  // The palette is centred and starts 12vh down (styles.css:731-742), so the default 1280x720
  // viewport centre — where `body.click()` used to leave the cursor — sits inside the results
  // list. Each result highlights itself on mouseenter (command-search.tsx:208), so a
  // pointer-boundary event fired when the list renders under a stationary cursor moves `active`
  // off the first row and the `data-active` assertion below sees "false". That is the second
  // failure this spec produced under parallel load, and it is a different cause from the
  // navigation race handled further down.
  await page.mouse.move(8, 8);
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

  // Reopen and check Escape closes it — but only once the work page is actually interactive.
  // The shortcut listener is attached in a `useEffect` (command-search.tsx:68-81) on the
  // `AppShell` each route mounts, so between leaving the work list and the work detail page
  // rendering there is a window with no listener at all: a `keyboard.press` that lands in it is
  // swallowed, and Playwright then retries the *locator*, never the keypress, turning one lost
  // key into a 10s timeout. Waiting for content the remounted page renders — its own <h1>, and
  // the search trigger that `AppShell` renders beside `CommandSearch` (app-shell.tsx:51-66) —
  // closes that window without re-pressing anything.
  await expect(page.getByRole("heading", { name: title })).toBeVisible();
  await expect(page.getByRole("button", { name: /Search/ })).toBeVisible();
  await page.keyboard.press("Control+k");
  await expect(palette).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(palette).toBeHidden();
});
