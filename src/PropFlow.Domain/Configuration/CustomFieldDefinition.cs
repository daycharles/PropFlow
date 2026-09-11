using System.Text.Json;

namespace PropFlow.Domain.Configuration;

// PF-S03.01. v1 is deliberately closed, the same call the automation engine's condition/action
// vocabulary made (PropFlow.Domain.Automation.AutomationRule): an organization picks from a
// fixed set of field types rather than authoring a schema or an expression, so a custom field
// can never become an executable surface. AppliesTo (ConfigurationEntityType) starts with
// WorkItem only; a second entity type is a vocabulary addition, not a redesign.
public enum CustomFieldType { Text, Number, Date, Boolean, SingleSelect }

public sealed class CustomFieldDefinition : TenantEntity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CustomFieldDefinition(Guid organizationId, Guid id, string key, string name,
        ConfigurationEntityType appliesTo, CustomFieldType fieldType, IReadOnlyList<string>? options,
        bool isRequired, int sortOrder, DateTimeOffset createdAt)
        : base(organizationId, id)
    {
        if (!Enum.IsDefined(appliesTo)) throw new ArgumentOutOfRangeException(nameof(appliesTo));
        if (!Enum.IsDefined(fieldType)) throw new ArgumentOutOfRangeException(nameof(fieldType));
        Key = ValidateKey(key);
        Name = ValidateName(name);
        AppliesTo = appliesTo;
        FieldType = fieldType;
        Options = SerializeOptions(fieldType, options);
        IsRequired = isRequired;
        SortOrder = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
        CreatedAt = createdAt.ToUniversalTime();
    }

    // EF materialization only. The ids must be constructor parameters so EF binds them from the
    // columns — a parameterless ctor would hand TenantEntity Guid.Empty and trip its guard on
    // every read (same reasoning as AutomationRule's identical private ctor).
    private CustomFieldDefinition(Guid organizationId, Guid id) : base(organizationId, id) { }

    public string Key { get; private set; } = "";
    public string Name { get; private set; } = "";
    public ConfigurationEntityType AppliesTo { get; private set; }
    public CustomFieldType FieldType { get; private set; }
    public string Options { get; private set; } = "[]";
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<string> ReadOptions() => JsonSerializer.Deserialize<string[]>(Options, Json) ?? [];

    public void Rename(string name) => Name = ValidateName(name);
    public void Reorder(int sortOrder) => SortOrder = sortOrder >= 0 ? sortOrder : throw new ArgumentOutOfRangeException(nameof(sortOrder));
    public void SetRequired(bool isRequired) => IsRequired = isRequired;
    public void ReplaceOptions(IReadOnlyList<string>? options) => Options = SerializeOptions(FieldType, options);
    public void Archive() => IsArchived = true;

    /// <summary>
    /// Whether <paramref name="raw"/> is a legal value for this definition — the check PF-S03.02
    /// runs before writing a CustomFieldValue. A null/blank value is legal exactly when the field
    /// is not required.
    /// </summary>
    public bool Accepts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return !IsRequired;
        return FieldType switch
        {
            CustomFieldType.Text => raw.Length <= 2000,
            CustomFieldType.Number => decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            CustomFieldType.Date => DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out _),
            CustomFieldType.Boolean => bool.TryParse(raw, out _),
            CustomFieldType.SingleSelect => ReadOptions().Contains(raw, StringComparer.Ordinal),
            _ => false,
        };
    }

    private static string ValidateKey(string key)
    {
        var trimmed = key?.Trim().ToLowerInvariant() ?? "";
        if (trimmed.Length is 0 or > 50 || !System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^[a-z][a-z0-9_]*$"))
            throw new ArgumentException(
                "Key must contain 1 to 50 characters: lowercase letters, digits, or underscore, starting with a letter.",
                nameof(key));
        return trimmed;
    }

    private static string ValidateName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200
            ? name.Trim() : throw new ArgumentException("Name must contain 1 to 200 characters.", nameof(name));

    private static string SerializeOptions(CustomFieldType fieldType, IReadOnlyList<string>? options)
    {
        if (fieldType != CustomFieldType.SingleSelect)
        {
            if (options is { Count: > 0 }) throw new ArgumentException("Options only apply to a SingleSelect field.", nameof(options));
            return "[]";
        }
        if (options is null || options.Count == 0)
            throw new ArgumentException("A SingleSelect field requires at least one option.", nameof(options));
        var trimmed = options.Select(o => o?.Trim() ?? "").ToArray();
        if (trimmed.Any(o => o.Length is 0 or > 100))
            throw new ArgumentException("Each option must contain 1 to 100 characters.", nameof(options));
        if (trimmed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != trimmed.Length)
            throw new ArgumentException("Options must be unique.", nameof(options));
        return JsonSerializer.Serialize(trimmed, Json);
    }
}
