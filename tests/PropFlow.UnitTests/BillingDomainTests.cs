using PropFlow.Domain.Billing;
using PropFlow.Domain.Leasing;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class BillingDomainTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Lease = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static LeaseCharge Charge(decimal amount = 1000m, LeaseChargeType type = LeaseChargeType.Recurring) =>
        new(Organization, Guid.NewGuid(), Lease, type, "Monthly rent", amount, new DateOnly(2026, 3, 1));

    private static RecurringCharge Schedule(DateOnly startsOn, DateOnly? endsOn = null, int dayOfMonth = 1) =>
        new(Organization, Guid.NewGuid(), Lease, "Monthly rent", 1650m, dayOfMonth, startsOn, endsOn, Now);

    // --- LeaseCharge -------------------------------------------------------------

    [Fact]
    public void A_partial_payment_leaves_the_charge_partially_paid_and_still_owing()
    {
        var charge = Charge();
        charge.Apply(400m);
        Assert.Equal(LeaseChargeStatus.PartiallyPaid, charge.Status);
        Assert.Equal(400m, charge.AmountApplied);
        Assert.Equal(600m, charge.Outstanding);
    }

    [Fact]
    public void Payments_that_close_the_outstanding_balance_mark_the_charge_paid()
    {
        var charge = Charge();
        charge.Apply(400m);
        charge.Apply(600m);
        Assert.Equal(LeaseChargeStatus.Paid, charge.Status);
        Assert.Equal(0m, charge.Outstanding);
    }

    [Fact]
    public void A_charge_refuses_more_money_than_it_is_owed()
    {
        var charge = Charge();
        charge.Apply(900m);
        Assert.Throws<InvalidOperationException>(() => charge.Apply(200m));
        Assert.Equal(900m, charge.AmountApplied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_charge_refuses_a_non_positive_amount(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Charge().Apply(amount));
    }

    [Fact]
    public void Reversing_a_payment_puts_the_charge_back_into_debt()
    {
        var charge = Charge();
        charge.Apply(1000m);
        Assert.Equal(LeaseChargeStatus.Paid, charge.Status);
        charge.Reverse(250m);
        Assert.Equal(LeaseChargeStatus.PartiallyPaid, charge.Status);
        Assert.Equal(250m, charge.Outstanding);
        charge.Reverse(750m);
        Assert.Equal(LeaseChargeStatus.Open, charge.Status);
        Assert.Equal(1000m, charge.Outstanding);
    }

    [Fact]
    public void A_reversal_cannot_hand_back_more_than_was_applied()
    {
        var charge = Charge();
        charge.Apply(100m);
        Assert.Throws<InvalidOperationException>(() => charge.Reverse(101m));
    }

    [Fact]
    public void A_voided_charge_takes_no_money_and_money_already_applied_blocks_voiding()
    {
        var voided = Charge();
        voided.Void();
        Assert.Throws<InvalidOperationException>(() => voided.Apply(10m));

        var paid = Charge();
        paid.Apply(10m);
        Assert.Throws<InvalidOperationException>(() => paid.Void());
    }

    [Fact]
    public void A_charge_carries_at_most_one_late_fee_stamp()
    {
        var charge = Charge();
        charge.MarkLateFeeApplied(new DateOnly(2026, 3, 10));
        Assert.Equal(new DateOnly(2026, 3, 10), charge.LateFeeAppliedOn);
        Assert.Throws<InvalidOperationException>(() => charge.MarkLateFeeApplied(new DateOnly(2026, 4, 10)));
    }

    // --- ResidentPayment ---------------------------------------------------------

    private static ResidentPayment Payment(decimal amount = 1000m, string? providerReference = "ref-1") =>
        new(Organization, Guid.NewGuid(), Lease, Guid.NewGuid(), amount, new DateOnly(2026, 3, 1), "check 1", null, providerReference);

    [Fact]
    public void A_payment_settles_once_and_records_when()
    {
        var payment = Payment();
        payment.Settle(Now);
        Assert.Equal(ResidentPaymentStatus.Settled, payment.Status);
        Assert.Equal(Now, payment.SettledAt);
        Assert.Throws<InvalidOperationException>(() => payment.Settle(Now));
    }

    [Fact]
    public void A_settled_payment_cannot_then_fail_and_a_failed_one_keeps_its_reason()
    {
        var settled = Payment();
        settled.Settle(Now);
        Assert.Throws<InvalidOperationException>(() => settled.Fail("too late"));

        var failed = Payment();
        failed.Fail("insufficient funds");
        Assert.Equal(ResidentPaymentStatus.Failed, failed.Status);
        Assert.Equal("insufficient funds", failed.FailureReason);
    }

    [Fact]
    public void Only_a_settled_payment_refunds_and_only_up_to_what_is_unrefunded()
    {
        var submitted = Payment();
        Assert.Throws<InvalidOperationException>(() => submitted.Refund(10m));

        var payment = Payment();
        payment.Settle(Now);
        payment.Refund(400m);
        Assert.Equal(400m, payment.RefundedAmount);
        Assert.Equal(600m, payment.NetSettled);
        payment.Refund(600m);
        Assert.Equal(0m, payment.NetSettled);
        Assert.Throws<InvalidOperationException>(() => payment.Refund(1m));
    }

    [Fact]
    public void A_blank_provider_reference_is_stored_as_null_so_the_unique_index_ignores_it()
    {
        Assert.Null(Payment(providerReference: "   ").ProviderReference);
        Assert.Null(Payment(providerReference: null).ProviderReference);
        Assert.Equal("ref-1", Payment(providerReference: " ref-1 ").ProviderReference);
    }

    // --- RecurringCharge ---------------------------------------------------------

    [Fact]
    public void A_schedule_produces_one_due_date_per_month_from_its_start()
    {
        var schedule = Schedule(new DateOnly(2026, 1, 1));
        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)],
            schedule.DueDatesThrough(new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public void A_second_run_over_the_same_period_produces_nothing()
    {
        var schedule = Schedule(new DateOnly(2026, 1, 1));
        var first = schedule.DueDatesThrough(new DateOnly(2026, 3, 15));
        schedule.MarkGeneratedThrough(new DateOnly(2026, 3, 15));
        Assert.Equal(3, first.Count);
        Assert.Empty(schedule.DueDatesThrough(new DateOnly(2026, 3, 15)));
        Assert.Equal([new DateOnly(2026, 4, 1)], schedule.DueDatesThrough(new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void A_schedule_stops_at_its_end_date_and_generates_nothing_while_paused()
    {
        var ending = Schedule(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)], ending.DueDatesThrough(new DateOnly(2026, 6, 1)));

        var paused = Schedule(new DateOnly(2026, 1, 1));
        paused.Pause();
        Assert.Empty(paused.DueDatesThrough(new DateOnly(2026, 6, 1)));
        paused.Resume();
        Assert.NotEmpty(paused.DueDatesThrough(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Generation_cannot_move_backwards()
    {
        var schedule = Schedule(new DateOnly(2026, 1, 1));
        schedule.MarkGeneratedThrough(new DateOnly(2026, 3, 1));
        Assert.Throws<ArgumentException>(() => schedule.MarkGeneratedThrough(new DateOnly(2026, 2, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void A_schedule_refuses_a_day_that_is_not_in_every_month(int dayOfMonth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Schedule(new DateOnly(2026, 1, 1), dayOfMonth: dayOfMonth));
    }

    [Fact]
    public void A_schedule_refuses_an_end_before_its_start_and_a_non_positive_amount()
    {
        Assert.Throws<ArgumentException>(() => Schedule(new DateOnly(2026, 3, 1), new DateOnly(2026, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecurringCharge(Organization, Guid.NewGuid(), Lease, "Rent", 0m, 1, new DateOnly(2026, 1, 1), null, Now));
    }

    // --- Credit ------------------------------------------------------------------

    private static Credit NewCredit(decimal amount = 500m) =>
        new(Organization, Guid.NewGuid(), Lease, amount, "Goodwill", new DateOnly(2026, 3, 1), Now);

    [Fact]
    public void A_credit_can_be_spent_in_parts_and_closes_when_exhausted()
    {
        var credit = NewCredit();
        credit.Apply(200m);
        Assert.Equal(CreditStatus.Open, credit.Status);
        Assert.Equal(300m, credit.Remaining);
        credit.Apply(300m);
        Assert.Equal(CreditStatus.Applied, credit.Status);
        Assert.Equal(0m, credit.Remaining);
    }

    [Fact]
    public void A_credit_refuses_to_overspend_and_a_spent_credit_cannot_be_voided()
    {
        var credit = NewCredit();
        Assert.Throws<InvalidOperationException>(() => credit.Apply(501m));
        credit.Apply(1m);
        Assert.Throws<InvalidOperationException>(() => credit.Void());

        var unused = NewCredit();
        unused.Void();
        Assert.Equal(CreditStatus.Voided, unused.Status);
        Assert.Throws<InvalidOperationException>(() => unused.Apply(1m));
    }

    // --- LateFeeRule -------------------------------------------------------------

    private static LateFeeRule Rule(int graceDays = 5, decimal flat = 50m, decimal percent = 0m, decimal? maximum = null) =>
        new(Organization, Guid.NewGuid(), null, "Standard late fee", graceDays, flat, percent, maximum, Now);

    [Fact]
    public void A_flat_rule_charges_its_flat_amount_and_a_percentage_rule_scales_with_the_debt()
    {
        Assert.Equal(50m, Rule().FeeFor(1000m));
        Assert.Equal(50m, Rule(flat: 0m, percent: 5m).FeeFor(1000m));
        Assert.Equal(75m, Rule(flat: 25m, percent: 5m).FeeFor(1000m));
    }

    [Fact]
    public void A_rule_never_charges_more_than_its_maximum_and_charges_nothing_on_a_settled_debt()
    {
        Assert.Equal(60m, Rule(flat: 0m, percent: 10m, maximum: 60m).FeeFor(1000m));
        Assert.Equal(0m, Rule().FeeFor(0m));
        Assert.Equal(0m, Rule().FeeFor(-5m));
    }

    [Fact]
    public void The_fee_is_rounded_to_cents()
    {
        // 1000.33 * 7.5% = 75.02475 -> 75.02
        Assert.Equal(75.02m, Rule(flat: 0m, percent: 7.5m).FeeFor(1000.33m));
    }

    [Fact]
    public void A_rule_only_applies_after_its_grace_period_and_never_while_disabled()
    {
        var rule = Rule(graceDays: 5);
        var dueOn = new DateOnly(2026, 3, 1);
        Assert.False(rule.AppliesOn(dueOn, new DateOnly(2026, 3, 5)));
        Assert.False(rule.AppliesOn(dueOn, new DateOnly(2026, 3, 6)));
        Assert.True(rule.AppliesOn(dueOn, new DateOnly(2026, 3, 7)));
        rule.Disable();
        Assert.False(rule.AppliesOn(dueOn, new DateOnly(2026, 3, 7)));
    }

    [Fact]
    public void A_rule_that_would_charge_nothing_is_refused_at_construction()
    {
        Assert.Throws<ArgumentException>(() => Rule(flat: 0m, percent: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(flat: -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(percent: 101m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(maximum: 0m));
    }

    // --- PaymentRefund -----------------------------------------------------------

    [Fact]
    public void A_refund_requires_a_provider_reference_and_a_positive_amount()
    {
        Assert.Throws<ArgumentException>(() => new PaymentRefund(Organization, Guid.NewGuid(), Guid.NewGuid(), 10m, "  ", null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaymentRefund(Organization, Guid.NewGuid(), Guid.NewGuid(), 0m, "rf-1", null, Now));
        Assert.Throws<ArgumentException>(() => new PaymentRefund(Organization, Guid.NewGuid(), Guid.Empty, 10m, "rf-1", null, Now));
    }

    [Fact]
    public void Every_billing_entity_refuses_an_empty_organization()
    {
        Assert.Throws<ArgumentException>(() => new Credit(Guid.Empty, Guid.NewGuid(), Lease, 1m, "x", new DateOnly(2026, 1, 1), Now));
        Assert.Throws<ArgumentException>(() => new LateFeeRule(Guid.Empty, Guid.NewGuid(), null, "x", 0, 1m, 0m, null, Now));
        Assert.Throws<ArgumentException>(() => new RecurringCharge(Guid.Empty, Guid.NewGuid(), Lease, "x", 1m, 1, new DateOnly(2026, 1, 1), null, Now));
        Assert.Throws<ArgumentException>(() => new PaymentRefund(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1m, "rf", null, Now));
    }
}
