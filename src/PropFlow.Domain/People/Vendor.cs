namespace PropFlow.Domain.People;

public sealed class Vendor : TenantEntity
{
    public Vendor(Guid organizationId, Guid id, string name) : base(organizationId, id)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Vendor name must contain 1 to 200 characters.", nameof(name));
        Name = name.Trim();
    }

    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Trade { get; private set; }
    public string? Category { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void UpdateContact(string? email, string? phone, string? trade, string? category)
    {
        Email = Employee.NormalizeOptional(email, 254);
        Phone = Employee.NormalizeOptional(phone, 40);
        Trade = Employee.NormalizeOptional(trade, 100);
        Category = Employee.NormalizeOptional(category, 100);
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
