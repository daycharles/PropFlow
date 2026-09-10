import { expect, test } from "@playwright/test";

test("work list and detail stay usable at a phone viewport", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");
  // Keep the check independent of a running API: these are the exact work-list/detail class
  // contracts rendered by the application, exercised against the compiled Tidewater stylesheet.
  await page.locator("body").evaluate((body) => {
    body.innerHTML = `<main><header><a class="brand">PropFlow</a><nav><a>Work</a></nav></header><section class="panel"><div class="work-heading"><h1>Work</h1><button>Refresh</button></div><div class="table-wrap"><table><thead><tr><th>Work</th></tr></thead><tbody><tr><td data-label="Work"><a>Mobile HVAC inspection</a></td><td data-label="Status"><span class="badge">Assigned</span></td></tr></tbody></table></div></section><section class="detail-workspace"><div class="detail-heading"><h1>Mobile HVAC inspection</h1><span class="badge">Assigned</span></div><div class="detail-grid"><section class="panel"><button>Save details</button></section><section class="panel">Timeline</section></div></section></main>`;
  });
  const row = page.locator("tbody tr");
  await expect(row).toHaveCSS("display", "block");
  await expect(page.locator("main")).toHaveJSProperty("scrollWidth", 390);
  await expect
    .poll(() =>
      page
        .locator(".detail-grid")
        .evaluate((node) => getComputedStyle(node).gridTemplateColumns.split(" ").length),
    )
    .toBe(1);
  await expect(page.getByRole("button", { name: /save details/i })).toHaveCSS("min-height", "44px");
});
