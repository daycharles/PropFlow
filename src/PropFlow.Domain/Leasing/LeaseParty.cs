namespace PropFlow.Domain.Leasing;

public sealed class LeaseParty(Guid organizationId, Guid id, Guid leaseId, string fullName, string role, string? email)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public string FullName { get; private set; } = Required(fullName, nameof(fullName), 200);
    public string Role { get; private set; } = Required(role, nameof(role), 100);
    public string? Email { get; private set; } = string.IsNullOrWhiteSpace(email) ? null : Required(email, nameof(email), 254);
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
