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
    // FS-S08. One capability covers billing reads and writes: charges, credits, late-fee
    // rules, refunds and the lease balance are one accounting surface, and a resident reaches
    // their own charges and payments through the portal capabilities instead.
    public const string ManageBilling = "Billing.Manage";

    // FS-S09: one capability covers reading and posting to the ledger. It reaches Property
    // Manager through the Admin/PM branch of ForRole below, deliberately - there is no
    // Accountant role in the fixed matrix, and an organization that wants the ledger off a PM
    // revokes it per-organization through RoleCapabilityOverrides (PF-S01.04).
    public const string ManageAccounting = "Accounting.Manage";
    public const string ReadOwnerAccounting = "Accounting.OwnerRead";
    public const string ResidentPortalRead = "ResidentPortal.Read";
    public const string ResidentPortalRequest = "ResidentPortal.Request";
    // PF-S01.03: coarse-grained for now (granted wherever "All" is granted) - a real
    // organization-editable role -> capability matrix is PF-S01.04, not yet built.
    public const string ManageMembers = "Identity.ManageMembers";
    // PF-S03.01: custom fields, numbering, business hours, and notification preferences.
    // Deliberately separate from ManageCategories (Regional Manager keeps that one, not this).
    public const string ManageConfiguration = "Settings.ManageConfiguration";

    // FS-S05: applicant intake is split into two capabilities on purpose. ManageApplications is
    // the workflow - create an application, record consent, order screening, approve or deny -
    // and it reaches Regional Manager, who runs leasing in the field. ReadApplicantPii is the
    // separate gate on unmasked contact details, income and screening detail, and it does not.
    // One combined capability would make FS-S05's required PII-access tests vacuous: they would
    // assert that whoever can work an application can read its PII, which is the same claim
    // twice. The second constant is what gives GET /api/applications/{id}/pii (PF-S05.05) a
    // control that can actually be denied to a role that still holds the workflow.
    public const string ManageApplications = "Applications.Manage";   // intake, consent, screening, approve/deny
    public const string ReadApplicantPii   = "Applications.ReadPii";  // unmasked contact, income, screening detail

    public static readonly IReadOnlyList<string> All = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, MarkOnTheWay, ManageCategories, ManageTemplates, ManagePeople, ManageAssets, ManageIntegrations, SendResidentMessage, ManageAutomationRules, ManageAttachments, ManageProperties, ManageLeasing, ManageBilling, ManageAccounting, ReadOwnerAccounting, ResidentPortalRead, ResidentPortalRequest, ManageMembers, ManageConfiguration, ManageApplications, ReadApplicantPii];
    private static readonly string[] WorkManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageAssets, ManageAttachments];
    private static readonly string[] CategoryManagement = [ReadWork, AssignVendor, AssignEmployee, CreateWork, UpdateWork, ManageCategories, ManageAssets, ManageAttachments];

    public static IReadOnlyList<string> ForRole(string role, Guid? employeeId = null, Guid? vendorId = null) => role switch
    {
        "Organization Admin" or "Property Manager" => All.Where(x => x is not ResidentPortalRead and not ResidentPortalRequest).ToArray(),
        // Spread rather than extend CategoryManagement: that array is shared with any future
        // role arm, and ManageApplications is a Regional Manager decision, not a work-management
        // one. ReadApplicantPii is deliberately absent here - see the constants above.
        "Regional Manager" => [.. CategoryManagement, ManageApplications],
        "Maintenance Supervisor" => WorkManagement,
        "Read Only" => [ReadWork],
        // A field role is not usable until the control-plane membership names the employee/vendor
        // it represents. Endpoint scope checks then narrow this capability to assigned work.
        "Technician" when employeeId is not null => [ReadWork, MarkOnTheWay, ManageAttachments],
        "Vendor" when vendorId is not null => [ReadWork],
        "Technician" or "Vendor" => [],
        "Owner" => [ReadOwnerAccounting],
        "Resident" when employeeId is null && vendorId is null => [ResidentPortalRead, ResidentPortalRequest],
        _ => []
    };
}
