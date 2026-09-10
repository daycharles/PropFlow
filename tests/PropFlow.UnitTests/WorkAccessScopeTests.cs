using PropFlow.Application.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class WorkAccessScopeTests
{
    private static readonly Guid PropertyA = Guid.NewGuid();
    private static readonly Guid PropertyB = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly Guid Vendor = Guid.NewGuid();

    [Fact]
    public void Regional_access_is_limited_to_explicit_properties()
    {
        var scope = new WorkAccessScope(new HashSet<Guid> { PropertyA });
        Assert.True(scope.Allows(PropertyA, null, null, WorkScopeSubject.Regional));
        Assert.False(scope.Allows(PropertyB, null, null, WorkScopeSubject.Regional));
    }

    [Fact]
    public void Technician_requires_current_employee_assignment_and_property_can_narrow_it()
    {
        var scope = new WorkAccessScope(new HashSet<Guid> { PropertyA }, EmployeeId: Employee);
        Assert.True(scope.Allows(PropertyA, Employee, null, WorkScopeSubject.Technician));
        Assert.False(scope.Allows(PropertyB, Employee, null, WorkScopeSubject.Technician));
        Assert.False(scope.Allows(PropertyA, Guid.NewGuid(), null, WorkScopeSubject.Technician));
    }

    [Fact]
    public void Vendor_requires_current_vendor_assignment()
    {
        var scope = new WorkAccessScope(new HashSet<Guid>(), VendorId: Vendor);
        Assert.True(scope.Allows(PropertyA, null, Vendor, WorkScopeSubject.Vendor));
        Assert.False(scope.Allows(PropertyA, null, Guid.NewGuid(), WorkScopeSubject.Vendor));
    }

    [Fact]
    public void Missing_scope_is_a_deny_not_an_organization_grant()
    {
        Assert.False(WorkAccessScope.None.Allows(PropertyA, Employee, Vendor, WorkScopeSubject.Regional));
        Assert.False(WorkAccessScope.None.Allows(PropertyA, Employee, Vendor, WorkScopeSubject.Technician));
        Assert.False(WorkAccessScope.None.Allows(PropertyA, Employee, Vendor, WorkScopeSubject.Vendor));
    }
}
