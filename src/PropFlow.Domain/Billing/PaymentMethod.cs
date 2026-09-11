namespace PropFlow.Domain.Billing;

public enum PaymentMethodType { Card, Ach, Cash, Check }
public enum PaymentMethodStatus { Active, Inactive }

public sealed class PaymentMethod(Guid organizationId, Guid id, Guid residentId, PaymentMethodType type, string label, string? providerToken, string? lastFour, DateTimeOffset createdAt)
    : TenantEntity(organizationId, id)
{
    public Guid ResidentId { get; private set; } = residentId == Guid.Empty ? throw new ArgumentException("Resident is required.", nameof(residentId)) : residentId;
    public PaymentMethodType Type { get; private set; } = type;
    public string Label { get; private set; } = Required(label, nameof(label), 100);
    public string? ProviderToken { get; private set; } = string.IsNullOrWhiteSpace(providerToken) ? null : providerToken.Trim()[..Math.Min(providerToken.Trim().Length, 200)];
    public string? LastFour { get; private set; } = string.IsNullOrWhiteSpace(lastFour) ? null : lastFour.Trim()[..Math.Min(lastFour.Trim().Length, 4)];
    public PaymentMethodStatus Status { get; private set; } = PaymentMethodStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
    public void Deactivate() => Status = PaymentMethodStatus.Inactive;
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
