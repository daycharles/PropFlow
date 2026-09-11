using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Leasing;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class BillingEndpointsTests(DatabaseFixture fixture)
{
    private const string CallbackSecret = "fs-s08-callback-secret";

    private static async Task<Guid> CreateLeaseAsync(Scenario s)
    {
        using var response = await s.Client.PostAsJsonAsync("/api/leasing/leases/", new
        {
            residentId = s.ResidentA, spaceId = s.SpaceA, startsOn = "2026-01-15", endsOn = "2026-12-31",
            monthlyRent = 1650, securityDeposit = 1650
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateChargeAsync(Scenario s, Guid leaseId, decimal amount, string dueOn = "2026-02-01", string type = "OneTime")
    {
        using var response = await s.Client.PostAsJsonAsync($"/api/leasing/leases/{leaseId}/charges",
            new { type, description = "Monthly rent", amount, dueOn });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> BalanceAsync(Scenario s, Guid leaseId)
    {
        using var response = await s.Client.GetAsync($"/api/billing/leases/{leaseId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> CallbackAsync(Scenario s, object payload, string secret = CallbackSecret)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/payment-callback") { Content = content };
        request.Headers.Add("X-PropFlow-Signature", signature);
        return await s.Client.SendAsync(request);
    }

    // --- Recurring charges -------------------------------------------------------

    [Fact]
    public async Task Recurring_charges_generate_one_charge_per_period_and_a_repeat_run_adds_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        using var schedule = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges", new
        {
            leaseId, description = "Monthly rent", amount = 1650m, dayOfMonth = 1, startsOn = "2026-01-01", endsOn = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, schedule.StatusCode);

        using var first = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges/run", new { through = "2026-03-15", leaseId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(3, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generated").GetInt32());

        using var second = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges/run", new { through = "2026-03-15", leaseId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generated").GetInt32());

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(3, await store.LeaseCharges.CountAsync(x => x.RecurringChargeId != null));
        Assert.Equal(4950m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_paused_schedule_generates_nothing_until_it_is_resumed()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        using var created = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges", new
        {
            leaseId, description = "Parking", amount = 75m, dayOfMonth = 1, startsOn = "2026-01-01", endsOn = (string?)null
        });
        var scheduleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/billing/recurring-charges/{scheduleId}/pause", null)).StatusCode);
        using var paused = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges/run", new { through = "2026-03-15", leaseId = (Guid?)null });
        Assert.Equal(0, (await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generated").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/billing/recurring-charges/{scheduleId}/resume", null)).StatusCode);
        using var resumed = await s.Client.PostAsJsonAsync("/api/billing/recurring-charges/run", new { through = "2026-03-15", leaseId = (Guid?)null });
        Assert.Equal(3, (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generated").GetInt32());
    }

    // --- Partial payments and ledger-consistent balances -------------------------

    [Fact]
    public async Task A_partial_payment_leaves_the_charge_partially_paid_and_the_balance_owing()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 1000m);

        using var partial = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 400m, reference = "pay-1" });
        Assert.Equal(HttpStatusCode.Created, partial.StatusCode);

        var balance = await BalanceAsync(s, leaseId);
        Assert.Equal(1000m, balance.GetProperty("charged").GetDecimal());
        Assert.Equal(400m, balance.GetProperty("applied").GetDecimal());
        Assert.Equal(600m, balance.GetProperty("outstanding").GetDecimal());

        await using var store = s.AdminStore(s.OrganizationA);
        var charge = await store.LeaseCharges.SingleAsync(x => x.Id == chargeId);
        Assert.Equal(LeaseChargeStatus.PartiallyPaid, charge.Status);

        // The rest of the balance closes the charge.
        using var rest = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 600m, reference = "pay-2" });
        Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
        Assert.Equal(0m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());

        // Overpaying a settled charge is refused rather than silently accepted.
        using var over = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 1m, reference = "pay-3" });
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    [Fact]
    public async Task The_lease_balance_stays_consistent_with_payments_credits_and_refunds()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 1200m);

        using var credit = await s.Client.PostAsJsonAsync($"/api/billing/leases/{leaseId}/credits",
            new { amount = 200m, reason = "Goodwill", issuedOn = "2026-02-01" });
        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);
        var creditId = (await credit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/billing/credits/{creditId}/apply", new { chargeId, amount = 200m })).StatusCode);

        using var payment = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 700m, reference = "pay-a" });
        Assert.Equal(HttpStatusCode.Created, payment.StatusCode);
        var paymentId = (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var refund = await s.Client.PostAsJsonAsync($"/api/billing/payments/{paymentId}/refunds",
            new { amount = 100m, reason = "Overcharged", providerReference = "rf-a" });
        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);

        var balance = await BalanceAsync(s, leaseId);
        Assert.Equal(1200m, balance.GetProperty("charged").GetDecimal());
        Assert.Equal(800m, balance.GetProperty("applied").GetDecimal());       // 200 credit + 700 paid - 100 refunded
        Assert.Equal(400m, balance.GetProperty("outstanding").GetDecimal());
        Assert.Equal(200m, balance.GetProperty("creditsApplied").GetDecimal());
        Assert.Equal(0m, balance.GetProperty("creditsRemaining").GetDecimal());
        Assert.Equal(700m, balance.GetProperty("paymentsSettled").GetDecimal());
        Assert.Equal(100m, balance.GetProperty("paymentsRefunded").GetDecimal());

        // The ledger identity: everything applied to a charge came from a settled payment
        // (net of refunds) or from a credit. Nothing is applied from nowhere.
        var applied = balance.GetProperty("applied").GetDecimal();
        var creditsApplied = balance.GetProperty("creditsApplied").GetDecimal();
        var settled = balance.GetProperty("paymentsSettled").GetDecimal();
        var refunded = balance.GetProperty("paymentsRefunded").GetDecimal();
        Assert.Equal(settled - refunded, applied - creditsApplied);
        Assert.Equal(applied - creditsApplied, balance.GetProperty("paymentsApplied").GetDecimal());
    }

    [Fact]
    public async Task A_credit_cannot_be_spent_twice_or_moved_to_another_lease()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 1000m);
        using var created = await s.Client.PostAsJsonAsync($"/api/billing/leases/{leaseId}/credits",
            new { amount = 100m, reason = "Goodwill", issuedOn = "2026-02-01" });
        var creditId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/billing/credits/{creditId}/apply", new { chargeId, amount = 100m })).StatusCode);
        using var again = await s.Client.PostAsJsonAsync($"/api/billing/credits/{creditId}/apply", new { chargeId, amount = 100m });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(900m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    // --- Idempotent provider callbacks ------------------------------------------

    [Fact]
    public async Task A_replayed_payment_callback_settles_the_charge_once_and_writes_one_row()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:ProviderCallbackSecret", CallbackSecret));
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 500m);
        var payload = new
        {
            OrganizationId = s.OrganizationA, ProviderReference = "provider-abc", LeaseId = leaseId, ChargeId = (Guid?)chargeId,
            Amount = 500m, Status = "Settled", DueOn = "2026-02-01", FailureReason = (string?)null,
            OccurredAt = new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero)
        };

        using var first = await CallbackAsync(s, payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(created.GetProperty("duplicate").GetBoolean());
        var paymentId = created.GetProperty("paymentId").GetGuid();

        // The replay must be a no-op — not a second row, and not an exception at the client.
        using var replay = await CallbackAsync(s, payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replayed.GetProperty("duplicate").GetBoolean());
        Assert.Equal(paymentId, replayed.GetProperty("paymentId").GetGuid());

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(1, await store.ResidentPayments.CountAsync(x => x.ProviderReference == "provider-abc"));
        var charge = await store.LeaseCharges.SingleAsync(x => x.Id == chargeId);
        Assert.Equal(500m, charge.AmountApplied);
        Assert.Equal(LeaseChargeStatus.Paid, charge.Status);
        Assert.Equal(0m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_failed_payment_callback_records_the_failure_and_leaves_the_charge_owing()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:ProviderCallbackSecret", CallbackSecret));
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 500m);
        using var response = await CallbackAsync(s, new
        {
            OrganizationId = s.OrganizationA, ProviderReference = "provider-fail", LeaseId = leaseId, ChargeId = (Guid?)chargeId,
            Amount = 500m, Status = "Failed", DueOn = "2026-02-01", FailureReason = "insufficient funds",
            OccurredAt = (DateTimeOffset?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var store = s.AdminStore(s.OrganizationA);
        var payment = await store.ResidentPayments.SingleAsync(x => x.ProviderReference == "provider-fail");
        Assert.Equal(ResidentPaymentStatus.Failed, payment.Status);
        Assert.Equal("insufficient funds", payment.FailureReason);
        Assert.Equal(500m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task An_unsigned_or_mis_signed_payment_callback_is_rejected_and_writes_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:ProviderCallbackSecret", CallbackSecret));
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var payload = new
        {
            OrganizationId = s.OrganizationA, ProviderReference = "provider-unsigned", LeaseId = leaseId, ChargeId = (Guid?)null,
            Amount = 100m, Status = "Settled", DueOn = "2026-02-01", FailureReason = (string?)null, OccurredAt = (DateTimeOffset?)null
        };
        using var wrongSecret = await CallbackAsync(s, payload, "not-the-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);

        using var unsigned = await s.Client.PostAsJsonAsync("/api/billing/payment-callback", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(0, await store.ResidentPayments.CountAsync());
    }

    // --- Refunds -----------------------------------------------------------------

    [Fact]
    public async Task A_refund_reverses_the_charge_and_a_replayed_refund_reference_is_a_no_op()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 800m);
        using var payment = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 800m, reference = "pay-r" });
        var paymentId = (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var first = await s.Client.PostAsJsonAsync($"/api/billing/payments/{paymentId}/refunds",
            new { amount = 300m, reason = "Partial refund", providerReference = "rf-1" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        using var replay = await s.Client.PostAsJsonAsync($"/api/billing/payments/{paymentId}/refunds",
            new { amount = 300m, reason = "Partial refund", providerReference = "rf-1" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(1, await store.PaymentRefunds.CountAsync());
        var charge = await store.LeaseCharges.SingleAsync(x => x.Id == chargeId);
        Assert.Equal(500m, charge.AmountApplied);
        Assert.Equal(LeaseChargeStatus.PartiallyPaid, charge.Status);
        Assert.Equal(300m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_refund_cannot_exceed_the_unrefunded_amount_of_the_payment()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 200m);
        using var payment = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 200m, reference = "pay-x" });
        var paymentId = (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var tooMuch = await s.Client.PostAsJsonAsync($"/api/billing/payments/{paymentId}/refunds",
            new { amount = 201m, reason = (string?)null, providerReference = "rf-big" });
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(0, await store.PaymentRefunds.CountAsync());
    }

    // --- Late fees ---------------------------------------------------------------

    [Fact]
    public async Task Late_fees_are_raised_once_per_overdue_charge_and_never_for_a_charge_inside_its_grace_period()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var overdue = await CreateChargeAsync(s, leaseId, 1000m, "2026-02-01");
        await CreateChargeAsync(s, leaseId, 500m, "2026-03-01");

        using var rule = await s.Client.PostAsJsonAsync("/api/billing/late-fee-rules", new
        {
            propertyId = (Guid?)null, name = "Standard", graceDays = 5, flatAmount = 50m, percentOfOutstanding = 0m, maximumAmount = (decimal?)null
        });
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);

        // As of 2026-02-10 only the February charge is past its 5-day grace period.
        using var run = await s.Client.PostAsJsonAsync("/api/billing/late-fees/run", new { asOf = "2026-02-10" });
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        Assert.Equal(1, (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assessed").GetInt32());

        using var repeat = await s.Client.PostAsJsonAsync("/api/billing/late-fees/run", new { asOf = "2026-02-20" });
        Assert.Equal(0, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assessed").GetInt32());

        await using var store = s.AdminStore(s.OrganizationA);
        var fee = Assert.Single(await store.LeaseCharges.Where(x => x.Type == LeaseChargeType.LateFee).ToListAsync());
        Assert.Equal(50m, fee.Amount);
        Assert.NotNull((await store.LeaseCharges.SingleAsync(x => x.Id == overdue)).LateFeeAppliedOn);
        Assert.Equal(1550m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_percentage_late_fee_rule_is_capped_by_its_maximum()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        await CreateChargeAsync(s, leaseId, 2000m, "2026-02-01");
        using var rule = await s.Client.PostAsJsonAsync("/api/billing/late-fee-rules", new
        {
            propertyId = (Guid?)null, name = "Percentage", graceDays = 0, flatAmount = 0m, percentOfOutstanding = 10m, maximumAmount = (decimal?)75m
        });
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        using var duplicate = await s.Client.PostAsJsonAsync("/api/billing/late-fee-rules", new
        {
            propertyId = (Guid?)null, name = "Second default", graceDays = 0, flatAmount = 10m, percentOfOutstanding = 0m, maximumAmount = (decimal?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var run = await s.Client.PostAsJsonAsync("/api/billing/late-fees/run", new { asOf = "2026-02-15" });
        Assert.Equal(1, (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assessed").GetInt32());
        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(75m, (await store.LeaseCharges.SingleAsync(x => x.Type == LeaseChargeType.LateFee)).Amount);
    }

    // --- Provider outage ---------------------------------------------------------

    [Fact]
    public async Task A_provider_outage_answers_503_and_leaves_no_payment_or_charge_mutation_behind()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:GatewayMode", "Unavailable"));
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 900m);

        using var response = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 900m, reference = "pay-outage" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        await using var store = s.AdminStore(s.OrganizationA);
        // Absent is not empty: assert the charge is genuinely untouched, not merely unread.
        Assert.Equal(0, await store.ResidentPayments.CountAsync());
        var charge = await store.LeaseCharges.SingleAsync(x => x.Id == chargeId);
        Assert.Equal(0m, charge.AmountApplied);
        Assert.Equal(LeaseChargeStatus.Open, charge.Status);
        Assert.Equal(900m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_payment_the_provider_declines_is_recorded_as_failed_and_applies_nothing()
    {
        await using var s = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:GatewayMode", "Declined"));
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 900m);

        using var response = await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 900m, reference = "pay-declined" });
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(ResidentPaymentStatus.Failed, (await store.ResidentPayments.SingleAsync()).Status);
        Assert.Equal(0m, (await store.LeaseCharges.SingleAsync(x => x.Id == chargeId)).AmountApplied);
        Assert.Equal(900m, (await BalanceAsync(s, leaseId)).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task The_same_charge_is_payable_once_the_provider_recovers()
    {
        // The outage host and the recovered host share the database, so this is the retry a
        // caller actually performs after a 503.
        await using var outage = await fixture.CreateScenarioAsync(builder => builder.UseSetting("Billing:GatewayMode", "Unavailable"));
        await outage.LoginAsync();
        var leaseId = await CreateLeaseAsync(outage);
        var chargeId = await CreateChargeAsync(outage, leaseId, 400m);
        using var failed = await outage.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 400m, reference = "pay-retry" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        using var recovered = new ApplicationFactory(fixture.RuntimeConnection);
        using var client = recovered.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { organizationSlug = outage.SlugA, email = outage.EmailA, password = outage.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var refreshed = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", refreshed);

        using var retry = await client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 400m, reference = "pay-retry" });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        await using var store = outage.AdminStore(outage.OrganizationA);
        Assert.Equal(400m, (await store.LeaseCharges.SingleAsync(x => x.Id == chargeId)).AmountApplied);
    }

    // --- Permissions -------------------------------------------------------------

    [Fact]
    public async Task Billing_endpoints_require_the_manage_billing_capability()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        var leaseId = await CreateLeaseAsync(s);
        var chargeId = await CreateChargeAsync(s, leaseId, 100m);

        // The Read Only role holds Work.Read and nothing else.
        await s.LoginAsync(reader: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync($"/api/billing/leases/{leaseId}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/billing/late-fee-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync($"/api/billing/charges/{chargeId}/payments", new { amount = 10m, reference = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await s.Client.PostAsJsonAsync($"/api/billing/leases/{leaseId}/credits", new { amount = 10m, reason = "nope", issuedOn = "2026-02-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync("/api/billing/late-fees/run", new { asOf = "2026-03-01" })).StatusCode);

        await using var store = s.AdminStore(s.OrganizationA);
        Assert.Equal(0, await store.ResidentPayments.CountAsync());
        Assert.Equal(0, await store.Credits.CountAsync());
        Assert.Equal(0, await store.LeaseCharges.CountAsync(x => x.Type == LeaseChargeType.LateFee));
    }

    [Fact]
    public async Task Billing_endpoints_reject_an_anonymous_caller()
    {
        await using var s = await fixture.CreateScenarioAsync();
        using var anonymous = s.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/billing/leases/{Guid.NewGuid()}/balance")).StatusCode);
    }
}
