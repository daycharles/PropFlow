# PropFlow full-suite scope

## Purpose

PropFlow currently ships a maintenance and property-operations vertical slice. This document
defines the broader property-management suite so that future work can be implemented and tested
against a deliberate target rather than being treated as part of an undefined "full suite".

The target suite is multi-tenant, portfolio-aware, and suitable for property managers, owners,
residents, vendors, and staff. Existing maintenance functionality remains a core module; it is
not the whole product.

## Module inventory

### Foundation and administration

1. **Identity, organizations, and permissions** — users, teams, roles, capability policies,
   invitations, MFA/SSO hooks, session management, audit log, and tenant isolation.
2. **Portfolio and property management** — portfolios, properties, buildings, units/spaces,
   amenities, occupancy status, contacts, documents, and property settings.
3. **Configuration and workflow administration** — custom fields, statuses, categories,
   templates, numbering, approval rules, business hours, time zones, and notification settings.

### Leasing and resident lifecycle

4. **Marketing and availability** — listings, availability, inquiries, showing appointments,
   applicants, and lead-source tracking.
5. **Applications and screening** — application workflow, applicant consent, screening provider
   adapter, review/approval/denial, and immutable decision history.
6. **Leases and renewals** — lease parties, terms, charges, deposits, documents, signatures,
   renewals, notices, transfers, move-in, and move-out.
7. **Resident portal and service** — resident profile, household members, requests, announcements,
   documents, payments, communication preferences, and portal access controls.

### Financial operations

8. **Charges, rent, and payments** — recurring charges, one-time charges, credits, late fees,
   payment methods, receipts, refunds, failed payments, reconciliation, and delinquency workflow.
9. **Accounting** — chart of accounts, journal entries, accounts payable, accounts receivable,
   bank/cash accounts, period close, reversals, and export to an accounting system.
10. **Budgeting and owner accounting** — property budgets, approvals, actual-vs-budget reporting,
    owner statements, distributions, management fees, and owner portal access.

### Property operations

11. **Maintenance and work orders** — the existing work, vendor, employee, resident messaging,
    timeline, automation, asset, attachment, scheduling, and attention-queue capabilities.
12. **Inspections and turns** — inspection templates, checklists, findings, photos, move-in/out
    comparisons, unit turns, make-ready tasks, approvals, and charges.
13. **Preventive maintenance and assets** — asset registry, warranties, service plans, recurring
    work, meter readings, lifecycle costs, replacement planning, and compliance dates.
14. **Vendor and procurement management** — vendor onboarding, insurance/licenses, contracts,
    rate cards, bids, purchase orders, work authorization, invoices, and performance history.
15. **Compliance and risk** — required inspections, licenses, safety checks, violations, incidents,
    notices, remediation, evidence, retention, and escalation.

### Communications, insight, and platform

16. **Communications** — existing consent-aware email/SMS/outbox foundation expanded with inbox,
    campaigns, announcements, delivery tracking, provider callbacks, and message retention.
17. **Documents and e-signature** — document templates, generated lease/notice packets, versioning,
    access rules, signatures, expiration, and retention.
18. **Reporting and analytics** — operational, leasing, financial, occupancy, maintenance, vendor,
    and portfolio reports with filters, exports, scheduled delivery, and role-based visibility.
19. **Integrations** — real PMS, accounting, screening, payment, banking, messaging, calendar,
    e-signature, and utility adapters with mapping, reconciliation, retries, and sync health.
20. **Billing and subscription administration** — customer plan, active-door metering, invoices,
    entitlements, usage limits, trials, and administrator billing access.

## Definition of full-suite tested

The suite is not release-ready until every module has:

- tenant-isolated persistence and authorization tests;
- API contract tests for successful, invalid, duplicate, stale, and unauthorized requests;
- audit/history coverage for material changes;
- browser workflow tests for its primary user journeys;
- role-based tests for manager, leasing agent, accountant, maintenance coordinator, technician,
  vendor, owner, and resident access where applicable;
- failure and retry tests for external providers and asynchronous work;
- seeded demo data that exercises the happy path and at least one exception path;
- documented retention, export, and deletion behavior for regulated or financial data.

## Release gates

### Gate 1 — Core PMS foundation

Foundation, administration, portfolio/property management, configuration, residents, leases,
and documents. Test login, invitations, role boundaries, property setup, unit occupancy, lease
creation, renewal, move-in, move-out, and tenant isolation.

### Gate 2 — Financial suite

Charges, payments, accounting, budgeting, owner accounting, and billing administration. Test
double-entry balancing, idempotent payment callbacks, refunds, reconciliation, period close,
owner statements, and financial-data permissions.

### Gate 3 — Operations suite

Maintenance, inspections/turns, preventive maintenance/assets, vendors/procurement, and
compliance. Test request-to-resolution, inspection-to-turn, recurring work, vendor approval,
invoice matching, compliance escalation, and cross-module audit history.

### Gate 4 — Engagement and ecosystem

Resident portal, communications, reporting/analytics, e-signature, and real integrations. Test
resident self-service, consent, document signing, report visibility, bidirectional sync,
reconciliation, retries, and provider outage recovery.

### Gate 5 — Production release

Run the complete regression suite against a clean seeded environment, verify migrations from the
previous release, exercise backup/restore, check observability and security headers, validate
retention jobs, and complete a role-by-module navigation audit. Publish only after every gate is
green.

## Initial implementation order

Build in dependency order:

1. Foundation/admin and portfolio configuration.
2. Resident lifecycle, leasing, leases, documents, and resident portal.
3. Charges/payments, accounting, budgeting, and owner accounting.
4. Inspections/turns, preventive maintenance, vendor/procurement, and compliance.
5. Communications expansion, reporting, e-signature, and real integrations.
6. Billing administration and final cross-module regression/release hardening.

The current release should be renamed internally as the **Operations/Maintenance release** until
Gates 1–5 are actually implemented and tested.
