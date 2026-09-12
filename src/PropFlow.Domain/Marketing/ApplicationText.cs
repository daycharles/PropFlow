namespace PropFlow.Domain.Marketing;

// Shared bounded-string guards for the FS-S05 application aggregate. Separate from
// ListingValidation because these also reject control characters — every string FS-S05 stores
// is a single-line code, status or note that reaches a PDF adverse-action letter, and an
// embedded newline or control byte there is a rendering bug waiting to happen.
internal static class ApplicationText
{
    internal static string RequireSingleLine(string? value, string parameter, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"Value must contain 1 to {maxLength} characters.", parameter);
        foreach (var ch in trimmed)
            if (char.IsControl(ch))
                throw new ArgumentException("Value must not contain control characters.", parameter);
        return trimmed;
    }

    internal static string? OptionalSingleLine(string? value, string parameter, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireSingleLine(value, parameter, maxLength);

    // Multi-line is allowed here: a decision note and a provider summary are prose.
    internal static string? OptionalText(string? value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", parameter);
        return trimmed;
    }

    internal static Guid RequireId(Guid value, string parameter) =>
        value == Guid.Empty ? throw new ArgumentException("ID is required.", parameter) : value;
}
