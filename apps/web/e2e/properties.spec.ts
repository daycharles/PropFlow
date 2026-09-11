import { expect, test } from "@playwright/test";

const password = process.env.PLAYWRIGHT_DEMO_PASSWORD ?? "DemoPassword!123";

test("property management lists the tenant portfolio and supports property search", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.getByRole("link", { name: "Properties" }).click();
  await expect(page.getByRole("heading", { name: "Properties" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Tidewater Portfolio" })).toHaveCount(4);
  await expect(page.getByRole("cell", { name: "Harbor View Apartments" })).toBeVisible();

  await page.getByLabel("Search properties").fill("Harbor View");
  await expect(page.getByRole("cell", { name: "Harbor View Apartments" })).toHaveCount(1);
  await expect(page.getByRole("row").filter({ hasText: "Maple Court" })).toHaveCount(0);
  await page
    .getByRole("row")
    .filter({ hasText: "Harbor View Apartments" })
    .getByRole("button", { name: "Open details" })
    .click();
  await expect(page.getByText(/101 — Occupied|101 — Vacant/).first()).toBeVisible();
  const contactName = `Browser Contact ${Date.now()}`;
  await page.getByLabel("Property contact name").fill(contactName);
  await page.getByLabel("Property contact role").fill("Property manager");
  await page.getByLabel("Property contact email").fill("contact@example.test");
  await page.getByRole("button", { name: "Add contact" }).click();
  await expect(page.getByText("Contact added.")).toBeVisible();
  await expect(page.getByText(new RegExp(`${contactName} — Property manager`))).toBeVisible();
  await page.getByLabel("Property document title").fill("Browser property rules");
  await page.getByLabel("Property document URL").fill("https://docs.example.test/browser-rules");
  await page.getByLabel("Property document type").fill("Policy");
  await page.getByRole("button", { name: "Add document" }).click();
  await expect(page.getByText("Document added.")).toBeVisible();
  await expect(page.getByText(/Browser property rules — Policy/)).toBeVisible();
});

test("property management creates a portfolio and property", async ({ page }) => {
  const runId = Date.now();
  const portfolioName = `Browser Portfolio ${runId}`;
  const propertyName = `Browser Property ${runId}`;

  await page.goto("/");
  await page.getByLabel("Organization slug").fill("tidewater-demo");
  await page.getByLabel("Email").fill("demo-admin@tidewater.example.test");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByRole("link", { name: "Properties" }).click();

  await page.getByLabel("New portfolio name").fill(portfolioName);
  await page.getByRole("button", { name: "Create portfolio" }).click();
  await expect(page.getByText("Portfolio created.")).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept(`${portfolioName} Renamed`));
  await page
    .locator("table")
    .first()
    .getByRole("row")
    .filter({ hasText: portfolioName })
    .getByRole("button", { name: "Rename" })
    .click();
  await expect(page.getByText("Portfolio updated.")).toBeVisible();

  await page.getByLabel("Property portfolio").selectOption({ label: `${portfolioName} Renamed` });
  await page.getByLabel("New property name").fill(propertyName);
  await page.getByRole("button", { name: "Create property" }).click();
  await expect(page.getByText("Property created.")).toBeVisible();
  await expect(page.getByRole("cell", { name: propertyName })).toBeVisible();
  await page.getByLabel("Building property").selectOption({ label: propertyName });
  await page.getByLabel("New building name").fill("Leasing Building");
  await page.getByRole("button", { name: "Create building" }).click();
  await expect(page.getByText("Building created.")).toBeVisible();
  await page.getByLabel("Space property").selectOption({ label: propertyName });
  const spaceCode = `Unit-${runId}`;
  await page.getByLabel("New space code").fill(spaceCode);
  await page.getByRole("button", { name: "Create space" }).click();
  await expect(page.getByText("Space created.")).toBeVisible();
  const propertyRow = page.getByRole("row").filter({ hasText: propertyName });
  await propertyRow.getByRole("button", { name: "Open details" }).click();
  await expect(
    page.getByRole("heading", { name: `Property details: ${propertyName}` }),
  ).toBeVisible();
  await expect(page.getByText("Leasing Building")).toBeVisible();
  await expect(page.getByText(`${spaceCode} — Vacant`)).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept("Leasing Building Renamed"));
  await page
    .locator("li")
    .filter({ hasText: "Leasing Building" })
    .getByRole("button", { name: "Rename" })
    .click();
  await expect(page.getByText("Building updated.")).toBeVisible();
  page.once("dialog", (dialog) => dialog.accept(`${spaceCode}-Updated`));
  await page.getByRole("button", { name: "Edit code" }).click();
  await expect(page.getByText("Space updated.")).toBeVisible();
  const updatedName = `${propertyName} Updated`;
  await page.getByLabel("Edit property name").fill(updatedName);
  await page.getByRole("button", { name: "Save property" }).click();
  await expect(page.getByText("Property updated.")).toBeVisible();
  await expect(page.getByRole("cell", { name: updatedName })).toBeVisible();
  await page
    .getByRole("row")
    .filter({ hasText: updatedName })
    .getByRole("button", { name: "Archive" })
    .click();
  await expect(page.getByText("Property archived.")).toBeVisible();
  await expect(page.getByRole("cell", { name: updatedName })).toHaveCount(0);
});
