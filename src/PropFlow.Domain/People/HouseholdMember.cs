namespace PropFlow.Domain.People;

public sealed class HouseholdMember(Guid organizationId, Guid id, Guid residentId, string fullName, string relationship, string? email)
    : TenantEntity(organizationId, id)
{
    public Guid ResidentId { get; private set; } = residentId == Guid.Empty ? throw new ArgumentException("Resident is required.", nameof(residentId)) : residentId;
    public string FullName { get; private set; } = Required(fullName, nameof(fullName), 200);
    public string Relationship { get; private set; } = Required(relationship, nameof(relationship), 100);
    public string? Email { get; private set; } = string.IsNullOrWhiteSpace(email) ? null : Required(email, nameof(email), 254);
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
