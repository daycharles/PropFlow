namespace PropFlow.Domain.Properties;

public sealed class PropertyAmenity(Guid organizationId, Guid id, Guid propertyId, string name, string? details = null)
    : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public string Name { get; private set; } = Portfolio.Required(name, nameof(name), 120);
    public string? Details { get; private set; } = string.IsNullOrWhiteSpace(details) ? null : Portfolio.Required(details, nameof(details), 500);
    public bool IsArchived { get; private set; }
    public void Update(string name, string? details)
    {
        Name = Portfolio.Required(name, nameof(name), 120);
        Details = string.IsNullOrWhiteSpace(details) ? null : Portfolio.Required(details, nameof(details), 500);
    }
    public void Archive() => IsArchived = true;
    public void Restore() => IsArchived = false;
}
