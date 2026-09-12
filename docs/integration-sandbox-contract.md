# The PropFlow sandbox integration contract (v1)

What a server must serve for `SandboxIntegrationAdapter` to pull from it. This is a
**PropFlow-defined** contract, not a vendor's API — it exists so the adapter contract is proven
over a real network boundary (auth, non-success statuses, malformed payloads, per-connection
credentials), which the in-memory `MockIntegrationAdapter` cannot exercise.

Implemented by `src/PropFlow.Infrastructure/Integrations/SandboxIntegrationAdapter.cs`; the
canonical shapes it maps onto are `src/PropFlow.Application/Integrations/CanonicalRecords.cs`.
When this document and that code disagree, the code is right — fix this file.

> This is an **outbound** contract: what PropFlow calls. PropFlow's own HTTP surface is
> [`api.md`](api.md).

## Configuration

| Key | Environment form | Meaning |
| --- | --- | --- |
| `Integrations:Sandbox:BaseAddress` | `Integrations__Sandbox__BaseAddress` | Absolute base address of the sandbox source, e.g. `https://sandbox.example.test/propflow`. One per deployment. A trailing `/` is optional |
| `Integrations:Secrets:<connection id>` | `Integrations__Secrets__<connection id>` | The credential for **one** connection. The id is the connection's GUID in `D` form (`8f1d…-…-…`); configuration keys are case-insensitive, so upper-case works too |

The credential is resolved per connection through `IIntegrationSecretStore`
(`ConfiguredIntegrationSecretStore`), never from a one-per-process setting the way
`CommunicationsOptions.TwilioAuthToken` is. **There is no credential column on
`integrations."Connections"`** and no write path on the secret store: a credential reaches the
deployment out of band. A UI where an admin types one needs envelope encryption, key rotation and
a deletion path, which is a later story.

A connection whose credential is missing, empty, or whitespace-only fails its sync with
`No credential is configured for this connection. Set Integrations:Secrets:<id>.` — a recorded
failure on the connection's health, not an exception at startup.

## Request

```http
GET {BaseAddress}/snapshot
Authorization: Bearer <credential>
Accept: application/json
```

No request body, no query string. This is a whole-source snapshot every time: the adapter's
`Descriptor.SupportsIncrementalPull` is `false` and there is no cursor or watermark in v1.

## Response

`200` with a JSON object carrying five **optional** arrays:

```json
{
  "properties":   [ /* Property   */ ],
  "spaces":       [ /* Space      */ ],
  "occupancies":  [ /* Occupancy  */ ],
  "workOrders":   [ /* WorkOrder  */ ],
  "assets":       [ /* Asset      */ ]
}
```

- An **omitted or null array means "none right now"**, not "unsupported". The adapter's
  descriptor claims all five kinds, so a server that cannot produce a kind is declaring it empty
  — and the reconciler is entitled to retire links of that kind. Do not omit an array you simply
  failed to load; fail the request instead.
- Field names are camelCase and map one-for-one onto the `Canonical*` record properties. Matching
  is **case-insensitive**.
- A `null` **entry** inside an array is an error, not an empty record.
- Any extra field is ignored. If the response names its own source system, that is ignored too —
  PropFlow stamps `sourceSystem` as `"sandbox"` itself.
- Dates are ISO-8601: `YYYY-MM-DD` for date-only fields, a full offset-bearing timestamp
  (`2026-09-01T13:45:00+00:00`) for instants.
- Optional string fields may be `null`, absent, or whitespace; all three normalize to `null`.
  Required strings are trimmed.

### Records

`req` = must be present and non-blank, or the pull fails naming the field and index.

**`properties[]`** → `CanonicalProperty`

| Field | | Notes |
| --- | --- | --- |
| `externalId` | req | Stable identity in the source system |
| `name` | req | |
| `addressLine`, `city`, `region`, `postalCode` | opt | |
| `timeZone` | opt | IANA id (`America/New_York`). `Property.ValidateTimeZone` rejects anything without a `/`, so a Windows id will be a mapping conflict downstream, not a pull failure |

**`spaces[]`** → `CanonicalSpace`

| Field | | Notes |
| --- | --- | --- |
| `externalId` | req | |
| `propertyExternalId` | req | Must match a `properties[].externalId` in the same snapshot |
| `code` | req | Unit/space label — `"201"`, `"1A"` |
| `buildingExternalId` | opt | |

**`occupancies[]`** → `CanonicalOccupancy`

| Field | | Notes |
| --- | --- | --- |
| `externalId` | req | **Must be stable for the life of the tenancy** — see below |
| `spaceExternalId` | req | Must match a `spaces[].externalId` |
| `residentName` | req | |
| `email`, `phone` | opt | |
| `movedInOn` | req | `YYYY-MM-DD` |
| `movedOutOn` | opt | Null means the current occupancy |

**`workOrders[]`** → `CanonicalWorkOrder`

| Field | | Notes |
| --- | --- | --- |
| `externalId` | req | |
| `propertyExternalId` | req | |
| `spaceExternalId` | opt | |
| `title` | req | |
| `description` | opt | |
| `status` | req | The source's own status string; mapping it onto `WorkStatus` is the mapping profile's job (PF-S19.02), not the adapter's |
| `openedAt` | req | Offset-bearing timestamp |
| `closedAt` | opt | |

**`assets[]`** → `CanonicalAsset`

| Field | | Notes |
| --- | --- | --- |
| `externalId` | req | |
| `propertyExternalId` | req | |
| `spaceExternalId` | opt | |
| `name` | req | |
| `kind`, `manufacturer`, `model`, `serialNumber` | opt | |
| `installedOn` | opt | `YYYY-MM-DD` |

## The occupancy/resident rule — read this before implementing

`CanonicalOccupancy` carries a resident's **name, email and phone but no resident external id**
(`CanonicalRecords.cs`), while PropFlow's `Occupancy` requires a `ResidentId`
(`src/PropFlow.Domain/People/Occupancy.cs`). The reconciler (PF-S19.02/.03) closes that gap with a
**synthetic resident link id**:

```
"{occupancyExternalId}#resident"
```

built in one named place rather than spelled inline at each call site. Two consequences for
anyone serving this contract:

1. **An occupancy's `externalId` must be stable for the life of that tenancy.** A resident's
   identity in PropFlow is derived from it. Re-keying an occupancy orphans the resident that was
   reconciled from it and creates a second one.
2. **One occupancy record means one resident.** A joint tenancy is two occupancy records against
   the same space today.

A future `CanonicalResident` — or a `residentExternalId` field on the occupancy payload —
supersedes this rule **additively**: the reconciler prefers a real id when one is present and
falls back to the synthetic form, so links already written under a synthetic id stay valid and
are not rewritten. Nothing in this contract needs to change for that; a new optional field is
added and the old behaviour remains the fallback.

## Failure

Every failure surfaces as one short, single-line, PropFlow-authored message recorded as the
connection's `lastError` (`GET /api/integrations/{id}`). **A provider response body is never put
in that message** — a body can be large, multi-line, or echo the credential back.

| Condition | `lastError` |
| --- | --- |
| `Integrations:Sandbox:BaseAddress` unset or not absolute | `Set Integrations:Sandbox:BaseAddress to the sandbox source's absolute base address before syncing this connection.` |
| No credential for this connection | `No credential is configured for this connection. Set Integrations:Secrets:<id>.` |
| Transport failure (DNS, TLS, connection refused) | `The sandbox source could not be reached.` |
| Any non-2xx status | `The sandbox source returned <status>.` |
| Body is not valid JSON, or not JSON at all | `The sandbox source returned a malformed snapshot payload.` |
| Empty body | `The sandbox source returned an empty snapshot payload.` |
| `null` entry in an array | `The sandbox snapshot has a null entry at <kind>[<i>].` |
| Missing required field | `The sandbox snapshot is missing required field '<field>' at <kind>[<i>].` |

The sync itself is unaffected by *how* the pull failed: `SyncAsync` records the message, increments
`consecutiveFailures`, and returns `{ outcome: "Failed", … }`. PF-S19.06 turns a run of those into
provider-outage handling.

## Worked example

```json
{
  "properties": [
    { "externalId": "SB-PROP-1", "name": "Willow Park", "addressLine": "9 Willow Rd",
      "city": "Norfolk", "region": "VA", "postalCode": "23504", "timeZone": "America/New_York" }
  ],
  "spaces": [
    { "externalId": "SB-SPACE-1", "propertyExternalId": "SB-PROP-1", "code": "201",
      "buildingExternalId": "SB-BLDG-1" }
  ],
  "occupancies": [
    { "externalId": "SB-OCC-1", "spaceExternalId": "SB-SPACE-1", "residentName": "Dana Reyes",
      "email": "dana.reyes@example.test", "phone": null,
      "movedInOn": "2025-07-01", "movedOutOn": null }
  ],
  "workOrders": [
    { "externalId": "SB-WO-1", "propertyExternalId": "SB-PROP-1", "spaceExternalId": "SB-SPACE-1",
      "title": "Broken blind", "description": null, "status": "Open",
      "openedAt": "2026-09-01T13:45:00+00:00", "closedAt": null }
  ],
  "assets": [
    { "externalId": "SB-ASSET-1", "propertyExternalId": "SB-PROP-1", "spaceExternalId": null,
      "name": "Boiler", "kind": "Boiler", "manufacturer": "Weil-McLain", "model": "CGa",
      "serialNumber": "WM-7781", "installedOn": "2020-02-14" }
  ]
}
```

This exact payload is asserted by `tests/PropFlow.UnitTests/SandboxIntegrationAdapterTests.cs`.

## Versioning

v1 is additive-only: a later revision may add optional fields and optional arrays, and the adapter
ignores anything it does not know. Removing a field, renaming one, or making an optional field
required is a v2 and needs a second adapter or a negotiated version header — neither exists yet.
