namespace PropFlow.Domain.Properties;

public sealed class PropertyContact(Guid organizationId, Guid id, Guid propertyId, string fullName, string role, string? email, string? phone)
    : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public string FullName { get; private set; } = Required(fullName, nameof(fullName), 200);
    public string Role { get; private set; } = Required(role, nameof(role), 100);
    public string? Email { get; private set; } = Optional(email, 254);
    public string? Phone { get; private set; } = Optional(phone, 40);
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must be at most {max} characters.", nameof(value));
}
