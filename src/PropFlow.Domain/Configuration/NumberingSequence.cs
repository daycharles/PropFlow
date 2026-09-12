using System.Globalization;

namespace PropFlow.Domain.Configuration;

// PF-S03.04. One row per (organization, AppliesTo) - at most one active numbering scheme per
// entity type, enforced by a unique index, not by this type.
//
// NextValue is never read into memory and written back by C# code: two concurrent work-item
// creations racing a read-then-increment would be able to allocate the same number, which a
// numbering scheme exists specifically to prevent (a gap from a failed create is acceptable; a
// duplicate is not). The actual allocation is a single atomic SQL statement
// (EfWorkOperations.AllocateDisplayNumberAsync: `UPDATE ... SET "NextValue" = "NextValue" + 1
// ... RETURNING`), which Postgres's row lock serializes for free. NextValue here is read-only -
// materialized by EF for display ("the next number will be...") and by the SQL statement itself,
// never mutated through this class.
public sealed class NumberingSequence : TenantEntity
{
    private NumberingSequence(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static NumberingSequence Create(Guid organizationId, Guid id, ConfigurationEntityType appliesTo,
        string? prefix, int width, DateTimeOffset createdAt) => new(organizationId, id)
    {
        AppliesTo = Enum.IsDefined(appliesTo) ? appliesTo : throw new ArgumentOutOfRangeException(nameof(appliesTo)),
        Prefix = ValidatePrefix(prefix),
        Width = ValidateWidth(width),
        NextValue = 1,
        CreatedAt = createdAt.ToUniversalTime(),
    };

    public ConfigurationEntityType AppliesTo { get; private set; }
    public string Prefix { get; private set; } = "";
    public int Width { get; private set; }
    public long NextValue { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }

    public void Configure(string? prefix, int width)
    {
        Prefix = ValidatePrefix(prefix);
        Width = ValidateWidth(width);
    }

    // Pure formatting, unit-testable without a database. Static so the atomic SQL allocator
    // (EfWorkOperations.AllocateDisplayNumberAsync, which reads prefix/width/value straight out
    // of an UPDATE...RETURNING row, not a materialized NumberingSequence) uses this exact same
    // logic instead of a second copy of it - one definition, called from both places.
    public static string Format(string prefix, int width, long value) =>
        $"{prefix}{value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}";

    public string FormatNext() => Format(Prefix, Width, NextValue);

    private static string ValidatePrefix(string? prefix)
    {
        var trimmed = prefix?.Trim() ?? "";
        if (trimmed.Length > 20) throw new ArgumentException("Prefix must contain at most 20 characters.", nameof(prefix));
        return trimmed;
    }

    private static int ValidateWidth(int width) =>
        width is >= 1 and <= 10 ? width : throw new ArgumentOutOfRangeException(nameof(width), "Width must be between 1 and 10.");
}
