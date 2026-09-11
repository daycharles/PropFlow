namespace PropFlow.Application;

// PF-S01.04: the catalog a role-matrix admin screen needs to render every role, not just the
// ones an organization currently has members in. Kept in sync with Capabilities.ForRole's switch
// arms by hand for now - unifying the two into one source (an enum, or ForRole driven off this
// list) is worth doing once a second consumer needs it, not speculatively here.
public static class Roles
{
    public const string OrganizationAdmin = "Organization Admin";
    public const string PropertyManager = "Property Manager";
    public const string RegionalManager = "Regional Manager";
    public const string MaintenanceSupervisor = "Maintenance Supervisor";
    public const string ReadOnly = "Read Only";
    public const string Technician = "Technician";
    public const string Vendor = "Vendor";

    public static readonly IReadOnlyList<string> All =
        [OrganizationAdmin, PropertyManager, RegionalManager, MaintenanceSupervisor, ReadOnly, Technician, Vendor];
}
