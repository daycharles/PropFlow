namespace PropFlow.Domain.Accounting;

public sealed class Owner(Guid organizationId, Guid id, string name, string email) : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    public string Email { get; private set; } = Required(email, nameof(email), 254);
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException("Value is required.", name);
}

public sealed class PropertyOwnership(Guid organizationId, Guid id, Guid ownerId, Guid propertyId, decimal percentage)
    : TenantEntity(organizationId, id)
{
    public Guid OwnerId { get; private set; } = ownerId == Guid.Empty ? throw new ArgumentException("Owner is required.", nameof(ownerId)) : ownerId;
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public decimal Percentage { get; private set; } = percentage is > 0 and <= 100 ? percentage : throw new ArgumentOutOfRangeException(nameof(percentage));
}

public sealed class ManagementFeeRule(Guid organizationId, Guid id, Guid propertyId, decimal percentage, decimal minimum, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public decimal Percentage { get; private set; } = percentage is >= 0 and <= 100 ? percentage : throw new ArgumentOutOfRangeException(nameof(percentage));
    public decimal Minimum { get; private set; } = minimum >= 0 ? minimum : throw new ArgumentOutOfRangeException(nameof(minimum));
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
}

public sealed class Distribution(Guid organizationId, Guid id, Guid ownerId, Guid propertyId, decimal amount, DateOnly paidOn, DateTimeOffset createdAt) : TenantEntity(organizationId, id)
{
    public Guid OwnerId { get; private set; } = ownerId == Guid.Empty ? throw new ArgumentException("Owner is required.", nameof(ownerId)) : ownerId;
    public Guid PropertyId { get; private set; } = propertyId == Guid.Empty ? throw new ArgumentException("Property is required.", nameof(propertyId)) : propertyId;
    public decimal Amount { get; private set; } = amount > 0 ? amount : throw new ArgumentOutOfRangeException(nameof(amount));
    public DateOnly PaidOn { get; private set; } = paidOn;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt.ToUniversalTime();
}

public sealed class OwnerStatement(Guid organizationId, Guid id, Guid ownerId, Guid propertyId, DateOnly startsOn, DateOnly endsOn, decimal income, decimal expenses, decimal managementFee, decimal distributions, string sourceHash, DateTimeOffset generatedAt) : TenantEntity(organizationId, id)
{
    public Guid OwnerId { get; private set; } = ownerId;
    public Guid PropertyId { get; private set; } = propertyId;
    public DateOnly StartsOn { get; private set; } = startsOn;
    public DateOnly EndsOn { get; private set; } = endsOn >= startsOn ? endsOn : throw new ArgumentException("End date precedes start date.", nameof(endsOn));
    public decimal Income { get; private set; } = income;
    public decimal Expenses { get; private set; } = expenses;
    public decimal ManagementFee { get; private set; } = managementFee;
    public decimal Distributions { get; private set; } = distributions;
    public decimal NetOwnerAmount => Income - Expenses - ManagementFee - Distributions;
    public string SourceHash { get; private set; } = sourceHash;
    public DateTimeOffset GeneratedAt { get; private set; } = generatedAt.ToUniversalTime();
}
