namespace PropFlow.Domain.Work;

public sealed class SavedView(Guid organizationId, Guid id, Guid userId, string name, string filters, string columns, bool isDefault)
    : TenantEntity(organizationId, id)
{
    public Guid UserId { get; private set; } = userId != Guid.Empty
        ? userId : throw new ArgumentException("User is required.", nameof(userId));
    public string Name { get; private set; } = NormalizeName(name);
    public string Filters { get; private set; } = RequireJson(filters, nameof(filters));
    public string Columns { get; private set; } = RequireJson(columns, nameof(columns));
    public bool IsDefault { get; private set; } = isDefault;

    public void Update(string name, string filters, string columns, bool isDefault)
    {
        Name = NormalizeName(name);
        Filters = RequireJson(filters, nameof(filters));
        Columns = RequireJson(columns, nameof(columns));
        IsDefault = isDefault;
    }

    private static string NormalizeName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100
        ? name.Trim() : throw new ArgumentException("View name must contain 1 to 100 characters.", nameof(name));

    // A filter or column set is a handful of fields. Cap it so a saved view cannot be used to
    // stash an arbitrary blob (it is stored and returned on every list call).
    public const int MaxDefinitionLength = 8 * 1024;

    private static string RequireJson(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("View definition is required.", parameterName);
        if (value.Length > MaxDefinitionLength)
            throw new ArgumentException($"View definition must be at most {MaxDefinitionLength} characters.", parameterName);
        try { System.Text.Json.JsonDocument.Parse(value); return value; }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("View definition must be valid JSON.", parameterName); }
    }
}
