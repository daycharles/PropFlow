import type { FullConfig } from "@playwright/test";
import { PRIMARY_NAV } from "../lib/navigation";

/**
 * Compile every route once before the suite starts.
 *
 * `next dev` compiles a route on demand, on its first request. The first recorded run of this
 * suite failed `attention`, `integrations` and `nav` for exactly that reason — the page snapshots
 * showed Next's `Compiling` indicator and `Loading…` while a 10s `expect` timeout ran out. It is
 * not a product problem and it is not slowness worth a bigger timeout: it is one fixed cost per
 * route, and paying it here means no spec pays it.
 *
 * Deliberately *not* a `webServer` block: nothing here can start the stack. It is two processes
 * (the ASP.NET API and `next dev`), the CI job starts them itself
 * (.github/workflows/ci.yml:106-126), and `.claude/rules/web.md` records the no-`webServer`
 * design. This only warms a stack that is already up, which is the same precondition
 * `npm run e2e` already has.
 *
 * A warm-up that cannot reach the app is logged and skipped, never fatal: if the stack is down
 * the specs themselves report it far more clearly than a global-setup stack trace would.
 */
const placeholderId = "00000000-0000-0000-0000-000000000000";

export default async function warmRoutes(config: FullConfig) {
  const baseURL =
    config.projects[0]?.use?.baseURL ?? process.env.PLAYWRIGHT_BASE_URL ?? "http://127.0.0.1:3000";

  // The nav is the single source for the shipped destinations (lib/navigation.ts), so a route
  // added there is warmed without touching this file. The two detail routes are reached from a
  // list and never appear in the nav, so they are named explicitly; the id does not have to
  // exist — compiling the route is the whole point.
  const routes = [
    ...new Set([
      ...PRIMARY_NAV.map((item) => item.href),
      `/work/${placeholderId}`,
      `/assets/${placeholderId}`,
    ]),
  ];

  for (const route of routes) {
    const url = new URL(route, baseURL);
    const startedAt = Date.now();
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(60_000) });
      // Read the body, so the request is finished — and the route compiled — before the next one.
      await response.text();
      console.log(`warmed ${route} → ${response.status} in ${Date.now() - startedAt}ms`);
    } catch (cause) {
      const reason = cause instanceof Error ? cause.message : String(cause);
      console.warn(
        `could not warm ${route} (${reason}); the first spec to visit it pays the compile.`,
      );
    }
  }
}
