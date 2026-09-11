using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application;
using PropFlow.Application.Billing;
using PropFlow.Domain.Billing;
using PropFlow.Domain.Leasing;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S08. Charges, rent, payments and collections. The charge entity is Leasing.LeaseCharge
// and the payment entity is Leasing.ResidentPayment — both shipped in Gate 1 and both
// extended here rather than replaced, so a lease has exactly one charge ledger and one
// payment trail.
public static class BillingEndpoints
{
    private const int MaxCallbackBytes = 32 * 1024;
    // Processors are not consistent about casing; the signature, not the shape, is the control.
    private static readonly JsonSerializerOptions CallbackJson = new() { PropertyNameCaseInsensitive = true };

    public static void MapBillingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/billing").RequireAuthorization(Capabilities.ManageBilling);

        group.MapGet("/leases/{leaseId:guid}/balance", async (Guid leaseId, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Leases.AnyAsync(x => x.Id == leaseId, ct)) return Results.NotFound();
            return Results.Ok(await BalanceAsync(store, leaseId, ct));
        });

        group.MapGet("/leases/{leaseId:guid}/charges", async (Guid leaseId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.LeaseCharges.AsNoTracking().Where(x => x.LeaseId == leaseId).OrderBy(x => x.DueOn)
                .Select(x => new { x.Id, x.LeaseId, x.Type, x.Description, x.Amount, x.AmountApplied, Outstanding = x.Amount - x.AmountApplied, x.DueOn, x.Status, x.RecurringChargeId, x.LateFeeAppliedOn }).Take(500).ToListAsync(ct)));

        // --- Recurring charges -------------------------------------------------------
        group.MapGet("/leases/{leaseId:guid}/recurring-charges", async (Guid leaseId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.RecurringCharges.AsNoTracking().Where(x => x.LeaseId == leaseId).OrderBy(x => x.StartsOn).ToListAsync(ct)));

        group.MapPost("/recurring-charges", async (RecurringChargeRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await store.Leases.AnyAsync(x => x.Id == request.LeaseId, ct)) return Results.Problem(statusCode: 400, title: "Lease was not found");
            try
            {
                var schedule = new RecurringCharge(store.OrganizationId, Guid.NewGuid(), request.LeaseId, request.Description,
                    request.Amount, request.DayOfMonth, request.StartsOn, request.EndsOn, clock.GetUtcNow());
                store.RecurringCharges.Add(schedule);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/billing/recurring-charges/{schedule.Id}", schedule);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        group.MapPost("/recurring-charges/{id:guid}/pause", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await ChangeSchedule(id, store, ct, schedule => schedule.Pause()));
        group.MapPost("/recurring-charges/{id:guid}/resume", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await ChangeSchedule(id, store, ct, schedule => schedule.Resume()));

        // Generating twice for the same period is a no-op: the schedule's GeneratedThrough
        // high-water mark filters the due dates, and the unique index on
        // (OrganizationId, RecurringChargeId, DueOn) is the backstop if two runs race.
        group.MapPost("/recurring-charges/run", async (GenerateChargesRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var schedules = await store.RecurringCharges
                .Where(x => x.Status == RecurringChargeStatus.Active && (request.LeaseId == null || x.LeaseId == request.LeaseId)).ToListAsync(ct);
            var created = new List<Guid>();
            foreach (var schedule in schedules)
            {
                foreach (var dueOn in schedule.DueDatesThrough(request.Through))
                {
                    var charge = new LeaseCharge(store.OrganizationId, Guid.NewGuid(), schedule.LeaseId, LeaseChargeType.Recurring,
                        schedule.Description, schedule.Amount, dueOn, schedule.Id);
                    store.LeaseCharges.Add(charge);
                    created.Add(charge.Id);
                }
                schedule.MarkGeneratedThrough(request.Through);
            }
            try { await store.SaveChangesAsync(ct); }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            { return Results.Problem(statusCode: 409, title: "A charge already exists for one of these periods."); }
            return Results.Ok(new { generated = created.Count, chargeIds = created });
        });

        // --- Credits -----------------------------------------------------------------
        group.MapGet("/leases/{leaseId:guid}/credits", async (Guid leaseId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Credits.AsNoTracking().Where(x => x.LeaseId == leaseId).OrderByDescending(x => x.IssuedOn)
                .Select(x => new { x.Id, x.LeaseId, x.Amount, x.AppliedAmount, Remaining = x.Amount - x.AppliedAmount, x.Reason, x.IssuedOn, x.Status }).ToListAsync(ct)));

        group.MapPost("/leases/{leaseId:guid}/credits", async (Guid leaseId, CreditRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await store.Leases.AnyAsync(x => x.Id == leaseId, ct)) return Results.NotFound();
            try
            {
                var credit = new Credit(store.OrganizationId, Guid.NewGuid(), leaseId, request.Amount, request.Reason, request.IssuedOn, clock.GetUtcNow());
                store.Credits.Add(credit);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/billing/credits/{credit.Id}", credit);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        group.MapPost("/credits/{creditId:guid}/apply", async (Guid creditId, ApplyCreditRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var credit = await store.Credits.SingleOrDefaultAsync(x => x.Id == creditId, ct);
            if (credit is null) return Results.NotFound();
            var charge = await store.LeaseCharges.SingleOrDefaultAsync(x => x.Id == request.ChargeId, ct);
            if (charge is null) return Results.NotFound();
            if (charge.LeaseId != credit.LeaseId) return Results.Problem(statusCode: 400, title: "The charge belongs to a different lease.");
            try
            {
                credit.Apply(request.Amount);
                charge.Apply(request.Amount);
                await store.SaveChangesAsync(ct);
                return Results.Ok(new { credit, charge });
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        // --- Late fees ---------------------------------------------------------------
        group.MapGet("/late-fee-rules", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.LateFeeRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));

        group.MapPost("/late-fee-rules", async (LateFeeRuleRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.PropertyId is { } propertyId && !await store.Properties.AnyAsync(x => x.Id == propertyId, ct))
                return Results.Problem(statusCode: 400, title: "Property was not found");
            // PostgreSQL treats NULLs as distinct, so the unique index cannot hold the
            // "one organization-wide default" rule on its own.
            if (await store.LateFeeRules.AnyAsync(x => x.PropertyId == request.PropertyId, ct))
                return Results.Conflict("A late-fee rule already exists for this scope.");
            try
            {
                var rule = new LateFeeRule(store.OrganizationId, Guid.NewGuid(), request.PropertyId, request.Name, request.GraceDays,
                    request.FlatAmount, request.PercentOfOutstanding, request.MaximumAmount, clock.GetUtcNow());
                store.LateFeeRules.Add(rule);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/billing/late-fee-rules/{rule.Id}", rule);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        });

        // Idempotent by construction: a charge is stamped with LateFeeAppliedOn the first time
        // a fee is raised against it, and stamped charges are excluded from every later run.
        group.MapPost("/late-fees/run", async (RunLateFeesRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var rules = await store.LateFeeRules.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(ct);
            if (rules.Count == 0) return Results.Ok(new { assessed = 0, chargeIds = Array.Empty<Guid>() });
            var overdue = await (from charge in store.LeaseCharges
                                 join lease in store.Leases on new { charge.OrganizationId, LeaseId = charge.LeaseId } equals new { lease.OrganizationId, LeaseId = lease.Id }
                                 join space in store.Spaces on new { lease.OrganizationId, SpaceId = lease.SpaceId } equals new { space.OrganizationId, SpaceId = space.Id }
                                 where charge.Status != LeaseChargeStatus.Voided && charge.Status != LeaseChargeStatus.Paid
                                       && charge.Type != LeaseChargeType.LateFee && charge.LateFeeAppliedOn == null && charge.DueOn < request.AsOf
                                 select new { Charge = charge, space.PropertyId }).ToListAsync(ct);
            var created = new List<Guid>();
            foreach (var row in overdue)
            {
                var rule = rules.FirstOrDefault(x => x.PropertyId == row.PropertyId) ?? rules.FirstOrDefault(x => x.PropertyId is null);
                if (rule is null || !rule.AppliesOn(row.Charge.DueOn, request.AsOf)) continue;
                var fee = rule.FeeFor(row.Charge.Outstanding);
                if (fee <= 0) continue;
                var charge = new LeaseCharge(store.OrganizationId, Guid.NewGuid(), row.Charge.LeaseId, LeaseChargeType.LateFee,
                    $"Late fee — {row.Charge.Description}", fee, request.AsOf);
                store.LeaseCharges.Add(charge);
                row.Charge.MarkLateFeeApplied(request.AsOf);
                created.Add(charge.Id);
            }
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { assessed = created.Count, chargeIds = created });
        });

        // --- Payments ----------------------------------------------------------------
        // A partial payment is the normal case: the amount only has to fit inside the
        // charge's outstanding balance, not equal it.
        group.MapPost("/charges/{chargeId:guid}/payments", async (Guid chargeId, ChargePaymentRequest request, OperationsStore store,
            IPaymentGateway gateway, TimeProvider clock, CancellationToken ct) =>
        {
            var charge = await store.LeaseCharges.SingleOrDefaultAsync(x => x.Id == chargeId, ct);
            if (charge is null) return Results.NotFound();
            if (request.Amount <= 0) return Results.Problem(statusCode: 400, title: "Amount must be greater than zero.");
            if (charge.Status == LeaseChargeStatus.Voided) return Results.Problem(statusCode: 409, title: "A voided charge cannot take a payment.");
            if (request.Amount > charge.Outstanding) return Results.Problem(statusCode: 409, title: "The amount exceeds the outstanding balance of the charge.");
            var lease = await store.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == charge.LeaseId, ct);
            if (lease is null) return Results.Problem(statusCode: 409, title: "The lease for this charge no longer exists.");

            var reference = string.IsNullOrWhiteSpace(request.Reference) ? $"pf-{Guid.NewGuid():N}" : request.Reference.Trim();
            var result = await gateway.AuthorizeAsync(new PaymentGatewayRequest(store.OrganizationId, lease.Id, lease.ResidentId, request.Amount, reference), ct);
            // Provider outage: answer 503 and write nothing at all. A half-written payment
            // would have to be reconciled by hand later.
            if (result.Outcome == PaymentGatewayOutcome.Unavailable)
                return Results.Problem(statusCode: 503, title: result.FailureReason ?? "The payment provider is unavailable.");

            var payment = new ResidentPayment(store.OrganizationId, Guid.NewGuid(), lease.Id, lease.ResidentId, request.Amount,
                charge.DueOn, reference, charge.Id, result.ProviderReference ?? reference);
            store.ResidentPayments.Add(payment);
            if (result.Outcome == PaymentGatewayOutcome.Declined)
            {
                payment.Fail(result.FailureReason);
                await store.SaveChangesAsync(ct);
                return Results.Problem(statusCode: 402, title: result.FailureReason ?? "The payment was declined.");
            }
            payment.Settle(clock.GetUtcNow());
            charge.Apply(request.Amount);
            try { await store.SaveChangesAsync(ct); }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            { return Results.Problem(statusCode: 409, title: "A payment with this provider reference already exists."); }
            return Results.Created($"/api/billing/payments/{payment.Id}", payment);
        });

        group.MapPost("/payments/{paymentId:guid}/refunds", async (Guid paymentId, RefundRequest request, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var payment = await store.ResidentPayments.SingleOrDefaultAsync(x => x.Id == paymentId, ct);
            if (payment is null) return Results.NotFound();
            var reference = string.IsNullOrWhiteSpace(request.ProviderReference) ? $"rf-{Guid.NewGuid():N}" : request.ProviderReference.Trim();
            // Replay of the same refund reference is a no-op, matching the payment callback.
            var existing = await store.PaymentRefunds.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderReference == reference, ct);
            if (existing is not null) return Results.Ok(new { refund = existing, duplicate = true });
            try
            {
                payment.Refund(request.Amount);
                if (payment.ChargeId is { } chargeId)
                {
                    var charge = await store.LeaseCharges.SingleOrDefaultAsync(x => x.Id == chargeId, ct);
                    if (charge is null) return Results.Problem(statusCode: 409, title: "The linked charge no longer exists.");
                    charge.Reverse(request.Amount);
                }
                var refund = new PaymentRefund(store.OrganizationId, Guid.NewGuid(), payment.Id, request.Amount, reference, request.Reason, clock.GetUtcNow());
                store.PaymentRefunds.Add(refund);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/billing/refunds/{refund.Id}", new { refund, duplicate = false });
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            { return Results.Problem(statusCode: 409, title: "A refund with this provider reference already exists."); }
        });

        group.MapGet("/payments/{paymentId:guid}/refunds", async (Guid paymentId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.PaymentRefunds.AsNoTracking().Where(x => x.PaymentId == paymentId).OrderBy(x => x.IssuedAt).ToListAsync(ct)));

        MapPaymentCallback(app);
    }

    // Unauthenticated and HMAC-signed, exactly like /api/communications/provider-callback
    // (ProviderCallbackEndpoints.cs:16-26). A processor cannot hold a session cookie or a CSRF
    // token, so the path is exempted from CSRF in Program.cs and the signature is the control.
    private static void MapPaymentCallback(WebApplication app) =>
        app.MapPost("/api/billing/payment-callback", async (HttpRequest request, IConfiguration configuration, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.ContentLength is > MaxCallbackBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var signature = request.Headers["X-PropFlow-Signature"].ToString();
            var secret = configuration["Billing:ProviderCallbackSecret"];
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return Results.Unauthorized();
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body, ct);
            if (body.Length > MaxCallbackBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body.ToArray()));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature))) return Results.Unauthorized();

            PaymentCallbackPayload? payload;
            try { payload = JsonSerializer.Deserialize<PaymentCallbackPayload>(body.ToArray(), CallbackJson); }
            catch (JsonException) { return Results.BadRequest(); }
            if (payload is null || payload.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(payload.ProviderReference)
                || payload.LeaseId == Guid.Empty || payload.Amount <= 0
                || !Enum.TryParse<PaymentCallbackStatus>(payload.Status, true, out var status))
                return Results.BadRequest();

            var connection = configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connection)) return Results.Problem(statusCode: 503, title: "Database is not configured");
            await using var store = DatabaseProvisioner.CreateOperationsStore(connection, payload.OrganizationId);
            var reference = payload.ProviderReference.Trim();
            var occurredAt = payload.OccurredAt ?? clock.GetUtcNow();

            // Replay: the row already exists, so the callback is a no-op and answers 200.
            var existing = await store.ResidentPayments.SingleOrDefaultAsync(x => x.ProviderReference == reference, ct);
            if (existing is not null) return Results.Ok(new { paymentId = existing.Id, status = existing.Status.ToString(), duplicate = true });

            var lease = await store.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.LeaseId, ct);
            if (lease is null) return Results.NotFound();
            LeaseCharge? charge = null;
            if (payload.ChargeId is { } chargeId)
            {
                charge = await store.LeaseCharges.SingleOrDefaultAsync(x => x.Id == chargeId && x.LeaseId == lease.Id, ct);
                if (charge is null) return Results.NotFound();
            }
            var payment = new ResidentPayment(payload.OrganizationId, Guid.NewGuid(), lease.Id, lease.ResidentId, payload.Amount,
                payload.DueOn ?? DateOnly.FromDateTime(occurredAt.UtcDateTime), reference, charge?.Id, reference);
            store.ResidentPayments.Add(payment);
            try
            {
                if (status == PaymentCallbackStatus.Settled)
                {
                    payment.Settle(occurredAt);
                    if (charge is not null)
                    {
                        if (payload.Amount > charge.Outstanding) return Results.Problem(statusCode: 409, title: "The amount exceeds the outstanding balance of the charge.");
                        charge.Apply(payload.Amount);
                    }
                }
                else payment.Fail(payload.FailureReason);
                await store.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Two callbacks raced. The unique index on (OrganizationId, ProviderReference)
                // decided it; report the surviving row rather than an error.
                store.ChangeTracker.Clear();
                var winner = await store.ResidentPayments.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderReference == reference, ct);
                return winner is null ? Results.Problem(statusCode: 409, title: "The callback could not be applied.")
                    : Results.Ok(new { paymentId = winner.Id, status = winner.Status.ToString(), duplicate = true });
            }
            return Results.Created($"/api/billing/payments/{payment.Id}", new { paymentId = payment.Id, status = payment.Status.ToString(), duplicate = false });
        });

    private static async Task<LeaseBalance> BalanceAsync(OperationsStore store, Guid leaseId, CancellationToken ct)
    {
        var charges = await store.LeaseCharges.AsNoTracking().Where(x => x.LeaseId == leaseId && x.Status != LeaseChargeStatus.Voided)
            .Select(x => new { x.Amount, x.AmountApplied }).ToListAsync(ct);
        var credits = await store.Credits.AsNoTracking().Where(x => x.LeaseId == leaseId && x.Status != CreditStatus.Voided)
            .Select(x => new { x.Amount, x.AppliedAmount }).ToListAsync(ct);
        var settled = await store.ResidentPayments.AsNoTracking()
            .Where(x => x.LeaseId == leaseId && x.Status == ResidentPaymentStatus.Settled && x.ChargeId != null)
            .SumAsync(x => x.Amount, ct);
        var refunded = await (from refund in store.PaymentRefunds.AsNoTracking()
                              join payment in store.ResidentPayments.AsNoTracking() on new { refund.OrganizationId, PaymentId = refund.PaymentId } equals new { payment.OrganizationId, PaymentId = payment.Id }
                              where payment.LeaseId == leaseId && payment.ChargeId != null
                              select refund.Amount).SumAsync(ct);
        var charged = charges.Sum(x => x.Amount);
        var applied = charges.Sum(x => x.AmountApplied);
        var creditsApplied = credits.Sum(x => x.AppliedAmount);
        var creditsRemaining = credits.Sum(x => x.Amount - x.AppliedAmount);
        // The ledger identity FS-S08 is tested against: everything applied to a charge came
        // either from a settled payment (net of refunds) or from a credit.
        return new LeaseBalance(leaseId, charged, applied, charged - applied, credits.Sum(x => x.Amount), creditsApplied, creditsRemaining,
            settled, refunded, applied - creditsApplied, charged - applied - creditsRemaining);
    }

    private static async Task<IResult> ChangeSchedule(Guid id, OperationsStore store, CancellationToken ct, Action<RecurringCharge> change)
    {
        var schedule = await store.RecurringCharges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (schedule is null) return Results.NotFound();
        try { change(schedule); await store.SaveChangesAsync(ct); return Results.Ok(schedule); }
        catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public enum PaymentCallbackStatus { Settled, Failed }

public sealed record RecurringChargeRequest(Guid LeaseId, string Description, decimal Amount, int DayOfMonth, DateOnly StartsOn, DateOnly? EndsOn);
public sealed record GenerateChargesRequest(DateOnly Through, Guid? LeaseId);
public sealed record CreditRequest(decimal Amount, string Reason, DateOnly IssuedOn);
public sealed record ApplyCreditRequest(Guid ChargeId, decimal Amount);
public sealed record LateFeeRuleRequest(Guid? PropertyId, string Name, int GraceDays, decimal FlatAmount, decimal PercentOfOutstanding, decimal? MaximumAmount);
public sealed record RunLateFeesRequest(DateOnly AsOf);
public sealed record ChargePaymentRequest(decimal Amount, string? Reference);
public sealed record RefundRequest(decimal Amount, string? Reason, string? ProviderReference);
public sealed record PaymentCallbackPayload(Guid OrganizationId, string ProviderReference, Guid LeaseId, Guid? ChargeId,
    decimal Amount, string Status, DateOnly? DueOn, string? FailureReason, DateTimeOffset? OccurredAt);
public sealed record LeaseBalance(Guid LeaseId, decimal Charged, decimal Applied, decimal Outstanding, decimal CreditsIssued,
    decimal CreditsApplied, decimal CreditsRemaining, decimal PaymentsSettled, decimal PaymentsRefunded, decimal PaymentsApplied, decimal Balance);
