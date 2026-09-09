# PropFlow web

Next.js App Router client for the PropFlow API. Copy `.env.example` to `.env.local`, set the
HTTPS API origin, then run `npm install` and `npm run dev` from this directory.

All browser calls use same-origin `/api` paths. Next.js rewrites them to `PROPFLOW_API_ORIGIN`,
which keeps secure authentication and antiforgery cookies browser-visible. The client fetches a
CSRF token before every authentication state transition, refreshes it after login, and uses
`cache: no-store` for API/session reads.

Navigation and direct page access are checked against the capabilities returned by `/api/session`.
These UI checks improve the experience; the API remains the authorization boundary.
