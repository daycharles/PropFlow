# Foundation API

All responses use JSON. Business endpoints will be added after authenticated persistence exists.

| Method | Path | Result |
| --- | --- | --- |
| GET | /health/live | 200, `{ "status": "healthy", "service": "PropFlow.Api" }`; process liveness only |
| GET | /api/session | 401 without an authenticated cookie; 403 without one valid organization claim; otherwise organization ID |

Errors use ASP.NET Core Problem Details for application exceptions. Correlation uses the request trace ID. Unexpected errors are logged server-side without returning exception details. Cookie authentication returns 401/403 for API requests instead of redirects. Health is intentionally public.

No login endpoint or cookie-issuing mechanism is present in milestone 1. API OpenAPI generation will be added alongside the business contracts in milestone 2.
