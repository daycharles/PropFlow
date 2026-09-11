namespace PropFlow.Application;

public static class Capabilities
{
    public const string ReadWork = "Work.Read";
    public const string AssignVendor = "Work.AssignVendor";
    public const string ManageTemplates = "Communications.ManageTemplates";
    public const string AssignEmployee = "Work.AssignEmployee";
    public const string CreateWork = "Work.Create";
    public const string UpdateWork = "Work.Update";
    public const string MarkOnTheWay = "Work.MarkOnTheWay";
    public const string ManageCategories = "Settings.ManageCategories";
    public const string ManagePeople = "People.Manage";
    public const string ManageAssets = "Assets.Manage";
    public const string ManageIntegrations = "Integrations.Manage";
    public const string SendResidentMessage = "Communications.SendMessage";
    public const string ManageAutomationRules = "Settings.ManageAutomationRules";
    public const string ManageAttachments = "Work.ManageAttachments";
    public const string ManageProperties = "Properties.Manage";
    public const string ManageLeasing = "Leasing.Manage";
    // FS-S09: one capability covers reading and posting to the ledger. It reaches Property
    // Manager through the Admin/PM branch of ForRole below, deliberately - there is no
    // Accountant role in the fixed matrix, and an organization that wants the ledger off a PM
    // revokes it per-organization through RoleCapabilityOverrides (PF-S01.04).
    public const string ManageAccounting = "Accounting.Manage";
    public const string ResidentPortalRead = "ResidentPortal.Read";
    public const string ResidentPortalRequest = "ResidentPortal.Request";
    // PF-S01.03: coarse-grained for now (granted wherever "All" is granted) - a real
    // organization-editable role -> capability matrix is PF-S01.04, not yet built.
    public const string ManageMembers = "Identity.ManageMembers";

    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, MarkOnTheWay, ManageCategories, ManageTemplates, ManagePeople, ManageAssets, ManageIntegrations, SendResidentMessage, ManageAutomationRules, ManageAttachments, ManageProperties, ManageLeasing, ManageAccounting, ResidentPortalRead, ResidentPortalRequest, ManageMembers];
    private static readonly string[] WorkManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageAssets, ManageAttachments];
    private static readonly string[] CategoryManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageAssets, ManageAttachments];

    public static IReadOnlyList<string> ForRole(string role, Guid? employeeId = null, Guid? vendorId = null) => role switch
    {
        "Organization Admin" or "Property Manager" => All.Where(x => x is not ResidentPortalRead and not ResidentPortalRequest).ToArray(),
        "Regional Manager" => CategoryManagement,
        "Maintenance Supervisor" => WorkManagement,
        "Read Only" => [ReadWork],
        // A field role is not usable until the control-plane membership names the employee/vendor
        // it represents. Endpoint scope checks then narrow this capability to assigned work.
        "Technician" when employeeId is not null => [ReadWork, MarkOnTheWay, ManageAttachments],
        "Vendor" when vendorId is not null => [ReadWork],
        "Technician" or "Vendor" => [],
        "Resident" when employeeId is null && vendorId is null => [ResidentPortalRead, ResidentPortalRequest],
        _ => []
    };
}
