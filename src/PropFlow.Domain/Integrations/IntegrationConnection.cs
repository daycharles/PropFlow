namespace PropFlow.Domain.Integrations;

// A configured link to one external property-management system for one organization. Holds the
// health signals the Integration Health screen (PF-6.11) reads: enabled, last attempt, last
// success, consecutive failure count, last error. It does not store credentials — the mock
// adapter needs none, and a real adapter's secrets belong in the secret store, keyed by id.
public sealed class IntegrationConnection : TenantEntity
{
    public const int SourceSystemMaxLength = 64;
    public const int DisplayNameMaxLength = 128;
    public const int ErrorMaxLength = 1000;

    // EF materialization.
    private IntegrationConnection(Guid organizationId, Guid id) : base(organizationId, id) { }

    public IntegrationConnection(Guid organizationId, Guid id, string sourceSystem, string displayName)
        : base(organizationId, id)
    {
        SourceSystem = NormalizeSourceSystem(sourceSystem);
        DisplayName = RequireSingleLine(displayName, nameof(displayName), DisplayNameMaxLength);
    }

    public string SourceSystem { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset? LastAttemptedAt { get; private set; }
    public DateTimeOffset? LastSucceededAt { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public string? LastError { get; private set; }

    public void Rename(string displayName) =>
        DisplayName = RequireSingleLine(displayName, nameof(displayName), DisplayNameMaxLength);

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    public void BeginSync(DateTimeOffset now)
    {
        if (!IsEnabled) throw new InvalidOperationException("The connection is disabled.");
        LastAttemptedAt = now;
    }

    public void CompleteSync(DateTimeOffset now)
    {
        LastAttemptedAt = now;
        LastSucceededAt = now;
        ConsecutiveFailures = 0;
        LastError = null;
    }

    public void FailSync(DateTimeOffset now, string error)
    {
        LastAttemptedAt = now;
        ConsecutiveFailures++;
        LastError = RequireSingleLine(error, nameof(error), ErrorMaxLength);
    }

    // A source-system identifier is a machine token: lower-case, no whitespace or control
    // characters. "mock", "yardi", "appfolio".
    public static string NormalizeSourceSystem(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length is 0 || trimmed.Length > SourceSystemMaxLength)
            throw new ArgumentException($"Source system must contain 1 to {SourceSystemMaxLength} characters.", nameof(value));
        foreach (var ch in trimmed)
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                throw new ArgumentException("Source system must not contain whitespace or control characters.", nameof(value));
        return trimmed;
    }

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
}
