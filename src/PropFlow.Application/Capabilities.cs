namespace PropFlow.Application;

public static class Capabilities
{
    public const string ReadWork = "Work.Read";
    public const string AssignVendor = "Work.AssignVendor";

    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        "Organization Admin" or "Regional Manager" or "Property Manager" or "Maintenance Supervisor" => All,
        "Read Only" => [ReadWork],
        // Field/vendor access needs assignment-level scoping in milestone 3. Deny until implemented.
        "Technician" or "Vendor" => [],
        _ => []
    };
}
