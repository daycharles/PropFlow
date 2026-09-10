---
paths:
  - "apps/web/**"
---

# The Next.js client

`apps/web` is a real, running application, not a placeholder — whatever `README.md:31` and
`docs/architecture.md:5,10` still say. Verified 2026-09-09.

## Stack

Next **16.3.4** App Router, React **19.3.0**, Tailwind **4.1.13**, TypeScript **5.9.3**, ESLint
9 with `eslint-config-next`, Prettier 3 (`apps/web/package.json`). Strict TS
(`apps/web/tsconfig.json`). Routes live under `apps/web/app/`, shared helpers under
`apps/web/lib/`.

## The script set — know what is missing

```
dev      next dev
build    next build
lint     eslint .
format   prettier --check .        # --check: it FAILS, it does not rewrite
e2e      playwright test
```

**There is no `test` script and no `start` script.** Do not invent `npm test` or
`npm run start`; both fail. Component-level testing does not exist here yet — adding it is a
backlog decision, not a drive-by.

`format` being `--check` is the usual CI surprise: run `npx prettier --write .` yourself, then
`npm run format` to confirm.

## Playwright has no `webServer`

`apps/web/playwright.config.ts` defines `testDir: ./e2e`, a chromium project, and a `baseURL` of
`http://127.0.0.1:3000` — and **no `webServer` block** (`playwright.config.ts:3-16`). Nothing
starts the app for you. The API and `next dev` must already be running before `npm run e2e`; the
`e2e` CI job does that by hand at `.github/workflows/ci.yml:70-78`. Override the target with
`PLAYWRIGHT_BASE_URL`.

## Always go through the Next proxy

`next.config.ts:3-10` rewrites `/api/:path*` to `PROPFLOW_API_ORIGIN` (default
`https://localhost:5001`). That is deliberate: the session cookie is `__Host-PropFlow` and the
antiforgery cookie `__Host-PropFlow-CSRF`, both `Secure`, `HttpOnly`, `SameSite=Strict`
(`src/PropFlow.Api/Program.cs:55-58,69-72`). A `__Host-` cookie is origin-bound, so the browser
only sees it when the request looks same-origin.

**Never call the API cross-origin from the client.** Fetch relative `/api/...` paths and let the
rewrite carry it. Mutations need the `X-CSRF-TOKEN` header from `GET /api/auth/csrf`, re-fetched
after login (`docs/local-development.md:56-63`).

## Responsive layout (PF-4.09)

Plain CSS in `app/styles.css`, no Tailwind engine (the `@import "tailwindcss"` is inert — no
PostCSS config), so there is an explicit `box-sizing: border-box` reset at the top and one
breakpoint, `@media (max-width: 700px)`. On a phone the Work list `<table>` becomes a stack of
cards: `thead` is hidden and each `<td>` shows its own label via `td[data-label]::before`, so
the `<td>`s in `page.tsx` carry `data-label` / `.title-cell` / `.select-cell`. Column-header
sorting is replaced by the `.mobile-sort` `<select>` (hidden on wider screens; it has an
explicit `aria-label="Sort by"` so `getByLabel("Status")` in the specs doesn't match its
option text). Interactive controls are `min-height: 44px`. `e2e/mobile.spec.ts` locks the
no-horizontal-overflow + touch-target guarantees at 375px.

## AGENTS.md

`apps/web/CLAUDE.md` is a single `@AGENTS.md` include, chaining to the generated
`apps/web/AGENTS.md`. **Leave both alone** — that file is generated, and the repo-root
`AGENTS.md` is a separate Codex model/allowance policy, not a PropFlow process document.
