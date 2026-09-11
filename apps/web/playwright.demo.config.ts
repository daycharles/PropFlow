import { defineConfig } from "@playwright/test";
import base from "./playwright.config";

// Demo-run config for the merged M3+M4+M5+M6 walkthrough. Extends the committed config rather
// than editing it: Playwright has no --video CLI flag, so recording has to come from a config.
// `video: on` writes a .webm per test and `trace: on` writes a Trace Viewer archive; both are
// embedded in the HTML report, so the report IS the recording.
export default defineConfig({
  ...base,
  use: {
    ...base.use,
    video: { mode: "on", size: { width: 1280, height: 720 } },
    trace: "on",
    screenshot: "on",
  },
  reporter: [["list"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  outputDir: "test-results",
});
