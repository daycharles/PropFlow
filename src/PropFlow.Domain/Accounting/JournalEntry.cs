namespace PropFlow.Domain.Accounting;

// One side of one posting. Exactly one of Debit/Credit carries the amount; the other is zero.
// Negative amounts are refused outright, so "balanced" cannot be reached by cancelling signs.
public sealed class JournalLine(Guid organizationId, Guid id, Guid entryId, Guid accountId, decimal debit, decimal credit, string? memo, Guid? propertyId = null)
    : TenantEntity(organizationId, id)
{
    public Guid EntryId { get; private set; } = entryId == Guid.Empty ? throw new ArgumentException("Journal entry is required.", nameof(entryId)) : entryId;
    public Guid AccountId { get; private set; } = accountId == Guid.Empty ? throw new ArgumentException("Account is required.", nameof(accountId)) : accountId;
    public Guid? PropertyId { get; private set; } = propertyId == Guid.Empty ? null : propertyId;
    public decimal Debit { get; private set; } = ValidateSides(debit, credit);
    public decimal Credit { get; private set; } = credit;
    public string? Memo { get; private set; } = string.IsNullOrWhiteSpace(memo)
        ? null
        : memo.Trim().Length <= 500 ? memo.Trim() : throw new ArgumentException("Memo must contain at most 500 characters.", nameof(memo));

    private static decimal ValidateSides(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0) throw new ArgumentException("A journal line cannot carry a negative amount.", nameof(debit));
        if (debit > 0 == credit > 0) throw new ArgumentException("A journal line carries exactly one of a debit or a credit.", nameof(debit));
        return debit;
    }
}

// A posted, immutable journal entry.
//
// Two decisions are load-bearing and are enforced here rather than at the endpoint:
//   1. The entry cannot be *constructed* unless its lines balance. There is no unbalanced
//      intermediate state and no Draft -> Posted flip, which is what lets the JournalEntries /
//      JournalLines tables be append-only (GRANT SELECT, INSERT only, plus a PostgreSQL trigger).
//   2. A correction is a new entry carrying ReversalOfId, never an edit. Because the original
//      can never be touched, "already reversed" is enforced by a unique index on
//      (OrganizationId, ReversalOfId) rather than by a flag on the original.
// The private constructor is the EF materialisation path (Attachment.cs uses the same shape);
// Post is the only way application code can make one.
public sealed class JournalEntry : TenantEntity
{
    private readonly List<JournalLine> lines = [];

    private JournalEntry(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Guid PeriodId { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string Reference { get; private set; } = "";
    public string? Memo { get; private set; }
    public decimal Total { get; private set; }
    public Guid PostedBy { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }
    public Guid? ReversalOfId { get; private set; }
    public IReadOnlyList<JournalLine> Lines => lines;

    public static JournalEntry Post(Guid organizationId, Guid id, FiscalPeriod period, DateOnly entryDate,
        string reference, string? memo, Guid postedBy, DateTimeOffset postedAt,
        IReadOnlyCollection<JournalLine> lines, Guid? reversalOfId = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(lines);
        if (period.OrganizationId != organizationId) throw new ArgumentException("The fiscal period belongs to another organization.", nameof(period));
        // The closed-period guard. InvalidOperationException, not ArgumentException, so the
        // endpoint maps it to 409 rather than 400 — the request is well formed, the books are shut.
        if (period.IsClosed) throw new InvalidOperationException("The fiscal period is closed; post the correction into an open period.");
        if (!period.Covers(entryDate)) throw new ArgumentException("The entry date falls outside the fiscal period.", nameof(entryDate));
        if (postedBy == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(postedBy));
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 100)
            throw new ArgumentException("Reference must contain 1 to 100 characters.", nameof(reference));
        if (memo is not null && memo.Trim().Length > 1000)
            throw new ArgumentException("Memo must contain at most 1000 characters.", nameof(memo));
        if (lines.Count < 2) throw new ArgumentException("A journal entry needs at least two lines.", nameof(lines));
        if (lines.Any(x => x.OrganizationId != organizationId || x.EntryId != id))
            throw new ArgumentException("Every line must belong to this entry and organization.", nameof(lines));
        if (lines.Select(x => x.Id).Distinct().Count() != lines.Count)
            throw new ArgumentException("Journal line identifiers must be unique.", nameof(lines));

        var debits = lines.Sum(x => x.Debit);
        var credits = lines.Sum(x => x.Credit);
        if (debits != credits)
            throw new ArgumentException($"A journal entry must balance: debits {debits} do not equal credits {credits}.", nameof(lines));
        if (debits <= 0) throw new ArgumentException("A journal entry must move a non-zero amount.", nameof(lines));

        var entry = new JournalEntry(organizationId, id)
        {
            PeriodId = period.Id,
            EntryDate = entryDate,
            Reference = reference.Trim(),
            Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
            Total = debits,
            PostedBy = postedBy,
            PostedAt = postedAt.ToUniversalTime(),
            ReversalOfId = reversalOfId == Guid.Empty ? null : reversalOfId
        };
        entry.lines.AddRange(lines);
        return entry;
    }

    // The audited correction path: debits and credits swap, the new entry points at this one,
    // and this one is left untouched.
    public JournalEntry Reverse(Guid id, FiscalPeriod period, DateOnly entryDate, Guid postedBy, DateTimeOffset postedAt)
    {
        if (ReversalOfId is not null) throw new InvalidOperationException("A reversing entry cannot itself be reversed.");
        if (id == Id) throw new ArgumentException("A reversing entry needs its own identifier.", nameof(id));
        var reversed = lines
            .Select(line => new JournalLine(OrganizationId, Guid.NewGuid(), id, line.AccountId, line.Credit, line.Debit, line.Memo, line.PropertyId))
            .ToArray();
        var reference = $"REV-{Reference}";
        return Post(OrganizationId, id, period, entryDate, reference.Length > 100 ? reference[..100] : reference,
            $"Reversal of {Reference}", postedBy, postedAt, reversed, reversalOfId: Id);
    }
}
