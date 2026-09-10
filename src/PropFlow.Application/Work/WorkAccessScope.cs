namespace PropFlow.Application.Work;

/// <summary>
/// The narrowest authority a membership may have over work. This is separate from capabilities:
/// a capability says what an actor may do, this scope says which work.
/// Empty scopes never mean organization-wide access.
/// </summary>
public sealed record WorkAccessScope(IReadOnlySet<Guid> PropertyIds, Guid? EmployeeId = null, Guid? VendorId = null)
{
    public static WorkAccessScope None { get; } = new(new HashSet<Guid>());

    public bool Allows(Guid propertyId, Guid? assignedEmployeeId, Guid? assignedVendorId, WorkScopeSubject subject)
    {
        if (propertyId == Guid.Empty) return false;
        // Field roles require their current assignment. A property grant can narrow it, never replace it.
        return subject switch
        {
            WorkScopeSubject.Regional => PropertyIds.Contains(propertyId),
            WorkScopeSubject.Technician => EmployeeId is { } employee && assignedEmployeeId == employee && (PropertyIds.Count == 0 || PropertyIds.Contains(propertyId)),
            WorkScopeSubject.Vendor => VendorId is { } vendor && assignedVendorId == vendor && (PropertyIds.Count == 0 || PropertyIds.Contains(propertyId)),
            _ => false
        };
    }
}

public enum WorkScopeSubject { Regional, Technician, Vendor }
