namespace PropFlow.Domain.People;

public sealed class Employee(Guid organizationId, Guid id, string displayName, string? email, string? phone) : TenantEntity(organizationId, id)
{
    public string DisplayName { get; private set; } = Normalize(displayName, nameof(displayName), 200);
    public string? Email { get; private set; } = NormalizeOptional(email, 254);
    public string? Phone { get; private set; } = NormalizeOptional(phone, 40);
    public bool IsActive { get; private set; } = true;

    public void UpdateContact(string displayName, string? email, string? phone)
    {
        DisplayName = Normalize(displayName, nameof(displayName), 200);
        Email = NormalizeOptional(email, 254);
        Phone = NormalizeOptional(phone, 40);
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    private static string Normalize(string value, string parameter, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() : throw new ArgumentException("A name is required.", parameter);
    internal static string? NormalizeOptional(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : throw new ArgumentException("Value is too long.");
}
