using PropFlow.Domain.Accounting;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class JournalEntryTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid RentRevenue = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset PostedAt = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EntryDate = new(2026, 1, 15);

    private static FiscalPeriod OpenPeriod() =>
        new(Organization, Guid.NewGuid(), "2026-01", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

    private static FiscalPeriod ClosedPeriod()
    {
        var period = OpenPeriod();
        period.Close(Actor, PostedAt);
        return period;
    }

    private static JournalLine[] BalancedLines(Guid entryId, decimal amount = 1650m) =>
    [
        new(Organization, Guid.NewGuid(), entryId, Cash, amount, 0m, "Rent received"),
        new(Organization, Guid.NewGuid(), entryId, RentRevenue, 0m, amount, "Rent earned")
    ];

    private static JournalEntry PostBalanced(FiscalPeriod period, Guid? entryId = null)
    {
        var id = entryId ?? Guid.NewGuid();
        return JournalEntry.Post(Organization, id, period, EntryDate, "JE-1001", "January rent", Actor, PostedAt, BalancedLines(id));
    }

    [Fact]
    public void A_balanced_entry_posts_with_its_lines_and_total()
    {
        var entry = PostBalanced(OpenPeriod());
        Assert.Equal(1650m, entry.Total);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(Actor, entry.PostedBy);
        Assert.Equal(PostedAt, entry.PostedAt);
        Assert.Null(entry.ReversalOfId);
        Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
    }

    [Fact]
    public void An_unbalanced_entry_cannot_be_constructed()
    {
        var period = OpenPeriod();
        var id = Guid.NewGuid();
        JournalLine[] lines =
        [
            new(Organization, Guid.NewGuid(), id, Cash, 1650m, 0m, null),
            new(Organization, Guid.NewGuid(), id, RentRevenue, 0m, 1600m, null)
        ];
        var exception = Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "JE-1001", null, Actor, PostedAt, lines));
        Assert.Contains("must balance", exception.Message);
    }

    [Fact]
    public void A_line_carries_exactly_one_of_a_debit_or_a_credit()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new JournalLine(Organization, Guid.NewGuid(), id, Cash, 10m, 10m, null));
        // Two zero sides would let an entry of zero lines-worth of money "balance" arithmetically.
        Assert.Throws<ArgumentException>(() => new JournalLine(Organization, Guid.NewGuid(), id, Cash, 0m, 0m, null));
    }

    [Fact]
    public void A_line_refuses_a_negative_amount()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new JournalLine(Organization, Guid.NewGuid(), id, Cash, -10m, 0m, null));
        Assert.Throws<ArgumentException>(() => new JournalLine(Organization, Guid.NewGuid(), id, Cash, 0m, -10m, null));
    }

    [Fact]
    public void An_entry_needs_at_least_two_lines()
    {
        var period = OpenPeriod();
        var id = Guid.NewGuid();
        JournalLine[] one = [new(Organization, Guid.NewGuid(), id, Cash, 100m, 0m, null)];
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "JE-1", null, Actor, PostedAt, one));
    }

    [Fact]
    public void Lines_must_belong_to_this_entry_and_this_organization()
    {
        var period = OpenPeriod();
        var id = Guid.NewGuid();
        JournalLine[] foreignEntry =
        [
            new(Organization, Guid.NewGuid(), Guid.NewGuid(), Cash, 10m, 0m, null),
            new(Organization, Guid.NewGuid(), id, RentRevenue, 0m, 10m, null)
        ];
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "JE-1", null, Actor, PostedAt, foreignEntry));

        JournalLine[] foreignOrganization =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), id, Cash, 10m, 0m, null),
            new(Organization, Guid.NewGuid(), id, RentRevenue, 0m, 10m, null)
        ];
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "JE-1", null, Actor, PostedAt, foreignOrganization));
    }

    [Fact]
    public void Posting_into_a_closed_period_raises_the_conflict_exception()
    {
        // InvalidOperationException, not ArgumentException: the endpoint maps this to 409, because
        // the request is well formed and it is the books that are shut.
        var exception = Assert.Throws<InvalidOperationException>(() => PostBalanced(ClosedPeriod()));
        Assert.Contains("closed", exception.Message);
    }

    [Fact]
    public void An_entry_dated_outside_its_period_is_refused()
    {
        var period = OpenPeriod();
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => JournalEntry.Post(Organization, id, period,
            new DateOnly(2026, 2, 1), "JE-1", null, Actor, PostedAt, BalancedLines(id)));
    }

    [Fact]
    public void An_entry_cannot_borrow_another_organizations_period()
    {
        var foreign = new FiscalPeriod(Guid.NewGuid(), Guid.NewGuid(), "2026-01", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, foreign, EntryDate, "JE-1", null, Actor, PostedAt, BalancedLines(id)));
    }

    [Fact]
    public void An_entry_requires_an_actor_and_a_reference()
    {
        var period = OpenPeriod();
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "JE-1", null, Guid.Empty, PostedAt, BalancedLines(id)));
        Assert.Throws<ArgumentException>(() =>
            JournalEntry.Post(Organization, id, period, EntryDate, "  ", null, Actor, PostedAt, BalancedLines(id)));
    }

    [Fact]
    public void Reversal_swaps_every_side_points_at_the_original_and_leaves_it_untouched()
    {
        var period = OpenPeriod();
        var original = PostBalanced(period);
        var reversalId = Guid.NewGuid();

        var reversal = original.Reverse(reversalId, period, new DateOnly(2026, 1, 20), Actor, PostedAt);

        Assert.Equal(original.Id, reversal.ReversalOfId);
        Assert.Equal(original.Total, reversal.Total);
        Assert.Equal(reversalId, reversal.Id);
        Assert.StartsWith("REV-", reversal.Reference);
        Assert.Contains(original.Reference, reversal.Memo);
        foreach (var line in reversal.Lines)
        {
            var source = original.Lines.Single(x => x.AccountId == line.AccountId);
            Assert.Equal(source.Debit, line.Credit);
            Assert.Equal(source.Credit, line.Debit);
            Assert.Equal(reversalId, line.EntryId);
        }
        // The original is never mutated — that is what lets the journal tables be append-only.
        Assert.Null(original.ReversalOfId);
        Assert.Equal(1650m, original.Lines.Single(x => x.AccountId == Cash).Debit);
    }

    [Fact]
    public void A_reversing_entry_cannot_itself_be_reversed()
    {
        var period = OpenPeriod();
        var reversal = PostBalanced(period).Reverse(Guid.NewGuid(), period, EntryDate, Actor, PostedAt);
        Assert.Throws<InvalidOperationException>(() => reversal.Reverse(Guid.NewGuid(), period, EntryDate, Actor, PostedAt));
    }

    [Fact]
    public void A_reversal_needs_its_own_identifier()
    {
        var period = OpenPeriod();
        var original = PostBalanced(period);
        Assert.Throws<ArgumentException>(() => original.Reverse(original.Id, period, EntryDate, Actor, PostedAt));
    }

    [Fact]
    public void A_reversal_into_a_closed_period_is_refused()
    {
        var original = PostBalanced(OpenPeriod());
        Assert.Throws<InvalidOperationException>(() => original.Reverse(Guid.NewGuid(), ClosedPeriod(), EntryDate, Actor, PostedAt));
    }
}
