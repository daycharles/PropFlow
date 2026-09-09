namespace PropFlow.Domain.Communications;

// Validation for message text fields. Single-line fields (subject, recipient address, name)
// reject every control character — a CR/LF there becomes SMTP/SMS header injection once a real
// provider is wired. Bodies allow newlines and tabs but nothing else in the control range.
public static class MessageText
{
    public static string RequireSingleLine(string? value, string parameter, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"Value must contain 1 to {maxLength} characters.", parameter);
        if (ContainsControlCharacter(trimmed, allowWhitespace: false))
            throw new ArgumentException("Value must not contain control characters.", parameter);
        return trimmed;
    }

    public static string RequireBody(string? value, string parameter, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"Value must contain 1 to {maxLength} characters.", parameter);
        if (ContainsControlCharacter(trimmed, allowWhitespace: true))
            throw new ArgumentException("Value must not contain control characters other than newlines and tabs.", parameter);
        return trimmed;
    }

    private static bool ContainsControlCharacter(string value, bool allowWhitespace)
    {
        foreach (var ch in value)
        {
            if (!char.IsControl(ch)) continue;
            if (allowWhitespace && ch is '\n' or '\r' or '\t') continue;
            return true;
        }
        return false;
    }
}
