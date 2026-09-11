import { expect, test, type Page } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";
const organizationSlug = "tidewater-demo";
const email = "demo-admin@tidewater.example.test";

async function signIn(page: Page) {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill(organizationSlug);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Work" })).toBeVisible();
}

async function createWork(page: Page, title: string) {
  return page.evaluate(async (title) => {
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
      body: JSON.stringify({ title, propertyId: property.id }),
    })) as { item: { id: string } };
    return body.item.id;
  }, title);
}

// A 1x1 PNG.
const pngBytes = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
  "base64",
);

// PF-7.01: attach a photo/document to a work order, download it, and remove it.
test("attach, list, download link, and remove a work-order file", async ({ page }) => {
  const title = `Attachment ${Date.now().toString(36)}`;
  await signIn(page);
  const workId = await createWork(page, title);

  await page.goto(`/work/${workId}`);
  await expect(page.getByRole("heading", { name: "Attachments" })).toBeVisible();
  await expect(page.getByText("No files attached yet.")).toBeVisible();

  await page.locator('.attachment-upload input[type="file"]').setInputFiles({
    name: "repair.png",
    mimeType: "image/png",
    buffer: pngBytes,
  });

  const item = page.locator(".attachment-list li").filter({ hasText: "repair.png" });
  await expect(item).toBeVisible();
  await expect(item.getByRole("link", { name: "repair.png" })).toHaveAttribute(
    "href",
    new RegExp(`/api/work/${workId}/attachments/`),
  );

  await item.getByRole("button", { name: "Remove repair.png" }).click();
  await expect(page.getByText("No files attached yet.")).toBeVisible();
});
