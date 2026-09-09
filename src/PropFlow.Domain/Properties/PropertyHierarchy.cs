namespace PropFlow.Domain.Properties;

public sealed class Portfolio(Guid organizationId, Guid id, string name) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    internal static string Required(string value, string parameter, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim()
        : throw new ArgumentException($"Value must contain 1 to {maximum} characters.", parameter);
}

public sealed class Property(Guid organizationId, Guid id, Guid portfolioId, string name, string timeZoneId)
    : TenantEntity(organizationId, id)
{
    public Guid PortfolioId { get; private set; } = RequiredId(portfolioId, nameof(portfolioId));
    public string Name { get; private set; } = Portfolio.Required(name, nameof(name), 200);
    public string TimeZoneId { get; private set; } = ValidateTimeZone(timeZoneId);

    private static Guid RequiredId(Guid id, string name) => id != Guid.Empty ? id : throw new ArgumentException("ID is required.", name);
    private static string ValidateTimeZone(string value)
    {
        var normalized = Portfolio.Required(value, nameof(value), 100);
        if (!normalized.Contains('/', StringComparison.Ordinal) || normalized.Contains(' ', StringComparison.Ordinal))
            throw new ArgumentException("An IANA time zone is required.", nameof(value));
        return normalized;
    }
}

public sealed class Building(Guid organizationId, Guid id, Guid propertyId, string name) : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId != Guid.Empty ? propertyId : throw new ArgumentException("Property is required.", nameof(propertyId));
    public string Name { get; private set; } = Portfolio.Required(name, nameof(name), 100);
}

public sealed class Space(Guid organizationId, Guid id, Guid propertyId, Guid? buildingId, string code) : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId != Guid.Empty ? propertyId : throw new ArgumentException("Property is required.", nameof(propertyId));
    public Guid? BuildingId { get; private set; } = buildingId;
    public string Code { get; private set; } = Portfolio.Required(code, nameof(code), 50);
}
