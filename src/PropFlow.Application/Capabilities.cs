namespace PropFlow.Application;

public static class Capabilities
{
    public const string ReadWork = "Work.Read";
    public const string AssignVendor = "Work.AssignVendor";
    public const string ManageTemplates = "Communications.ManageTemplates";
    public const string AssignEmployee = "Work.AssignEmployee";
    public const string CreateWork = "Work.Create";
    public const string UpdateWork = "Work.Update";
    public const string ManageCategories = "Settings.ManageCategories";
    public const string ManagePeople = "People.Manage";
    public const string ManageAssets = "Assets.Manage";
    public const string ManageIntegrations = "Integrations.Manage";

    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageTemplates, ManagePeople, ManageAssets, ManageIntegrations];
    private static readonly string[] WorkManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageAssets];
    private static readonly string[] CategoryManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageAssets];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        "Organization Admin" or "Property Manager" => All,
        "Regional Manager" => CategoryManagement,
        "Maintenance Supervisor" => WorkManagement,
        "Read Only" => [ReadWork],
        // Field/vendor access needs assignment-level scoping in milestone 3. Deny until implemented.
        "Technician" or "Vendor" => [],
        _ => []
    };
}
