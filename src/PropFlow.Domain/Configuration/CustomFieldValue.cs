namespace PropFlow.Domain.Configuration;

// PF-S03.02. One row per (work item, definition) — at most one value per field per work item,
// enforced by a unique index, not by this type. The raw string is validated against the live
// CustomFieldDefinition.Accepts() before either Create or SetValue runs, by the caller
// (EfWorkOperations), so this type never re-parses or re-derives the definition's rules; it only
// holds what already passed.
public sealed class CustomFieldValue : TenantEntity
{
    private CustomFieldValue(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static CustomFieldValue Create(Guid organizationId, Guid id, Guid workId, Guid customFieldDefinitionId,
        string value, DateTimeOffset updatedAt) => new(organizationId, id)
    {
        WorkId = workId == Guid.Empty ? throw new ArgumentException("Work item is required.", nameof(workId)) : workId,
        CustomFieldDefinitionId = customFieldDefinitionId == Guid.Empty
            ? throw new ArgumentException("Custom field definition is required.", nameof(customFieldDefinitionId))
            : customFieldDefinitionId,
        Value = RequireValue(value),
        UpdatedAt = updatedAt.ToUniversalTime(),
    };

    public Guid WorkId { get; private set; }
    public Guid CustomFieldDefinitionId { get; private set; }
    public string Value { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetValue(string value, DateTimeOffset updatedAt)
    {
        Value = RequireValue(value);
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    private static string RequireValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 2000
            ? value : throw new ArgumentException("Value must contain 1 to 2000 characters.", nameof(value));
}
