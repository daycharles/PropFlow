namespace PropFlow.Application;

public static class Capabilities
{
    public const string ReadWork = "Work.Read";
    public const string AssignVendor = "Work.AssignVendor";
    public const string ManageTemplates = "Communications.ManageTemplates";

    // Every capability that exists, for registering authorization policies.
    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor, ManageTemplates];

    private static readonly string[] WorkManagement = [ReadWork, AssignVendor];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        // Template management is limited to Organization Admin and Property Manager.
        "Organization Admin" or "Property Manager" => All,
        "Regional Manager" or "Maintenance Supervisor" => WorkManagement,
        "Read Only" => [ReadWork],
        // Field/vendor access needs assignment-level scoping in milestone 3. Deny until implemented.
        "Technician" or "Vendor" => [],
        _ => []
    };
}
