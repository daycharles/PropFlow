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
    public const string SendResidentMessage = "Communications.SendMessage";

    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageTemplates, ManagePeople, ManageAssets, ManageIntegrations, SendResidentMessage];
    private static readonly string[] WorkManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageAssets];
    private static readonly string[] CategoryManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageAssets];

    public static IReadOnlyList<string> ForRole(string role) => role switch
    {
        "Organization Admin" or "Property Manager" => All,
        "Regional Manager" => CategoryManagement,
        "Maintenance Supervisor" => WorkManagement,
        "Read Only" => [ReadWork],
        // PF-5.01 supplies the scope evaluator. PF-5.02 must only grant field capabilities after
        // an authenticated membership supplies a non-empty WorkAccessScope.
        "Technician" or "Vendor" => [],
        _ => []
    };
}
