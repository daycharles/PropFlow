namespace PropFlow.Domain.Properties;

public sealed class PropertyDocument(Guid organizationId, Guid id, Guid propertyId, string title, string documentUrl, string? documentType)
    : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public string Title { get; private set; } = Required(title, nameof(title), 200);
    public string DocumentUrl { get; private set; } = Required(documentUrl, nameof(documentUrl), 1000);
    public string? DocumentType { get; private set; } = Optional(documentType, 100);
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must be at most {max} characters.", nameof(value));
}
