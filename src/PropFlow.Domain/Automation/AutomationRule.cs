using System.Text.Json;
using PropFlow.Domain.Work;

namespace PropFlow.Domain.Automation;

// v1 is deliberately closed: administrators choose from these values rather than supplying
// expressions or scripts. The JSON payload keeps the persisted rule shape forward-compatible
// with a future visual builder without turning the database into an executable DSL.
public enum AutomationTrigger { WorkCreated, WorkStatusChanged }
public enum AutomationConditionKind { WorkStatusEquals, CategoryEquals }
public enum AutomationActionKind { SendResidentMessage, SetPriority }

public sealed record AutomationCondition(AutomationConditionKind Kind, WorkStatus? Status = null, Guid? CategoryId = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind == AutomationConditionKind.WorkStatusEquals && (Status is null || !Enum.IsDefined(Status.Value)))
            throw new ArgumentException("A valid work status is required.", nameof(Status));
        if (Kind == AutomationConditionKind.CategoryEquals && (CategoryId is not { } category || category == Guid.Empty))
            throw new ArgumentException("A category id is required.", nameof(CategoryId));
    }
}

public sealed record AutomationAction(AutomationActionKind Kind, Guid? TemplateId = null, WorkPriority? Priority = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind == AutomationActionKind.SendResidentMessage && (TemplateId is not { } template || template == Guid.Empty))
            throw new ArgumentException("A message template id is required.", nameof(TemplateId));
        if (Kind == AutomationActionKind.SetPriority && (Priority is null || !Enum.IsDefined(Priority.Value)))
            throw new ArgumentException("A valid priority is required.", nameof(Priority));
    }
}

public sealed class AutomationRule : TenantEntity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AutomationRule(Guid organizationId, Guid id, string name, AutomationTrigger trigger,
        IReadOnlyList<AutomationCondition>? conditions, IReadOnlyList<AutomationAction> actions, DateTimeOffset createdAt)
        : base(organizationId, id)
    {
        CreatedAt = createdAt.ToUniversalTime();
        Update(name, trigger, conditions, actions, createdAt);
    }

    private AutomationRule() : base(Guid.Empty, Guid.Empty) { }

    public string Name { get; private set; } = null!;
    public AutomationTrigger Trigger { get; private set; }
    public string Conditions { get; private set; } = "[]";
    public string Actions { get; private set; } = "[]";
    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AutomationCondition> ReadConditions() =>
        JsonSerializer.Deserialize<AutomationCondition[]>(Conditions, Json) ?? [];
    public IReadOnlyList<AutomationAction> ReadActions() =>
        JsonSerializer.Deserialize<AutomationAction[]>(Actions, Json) ?? [];

    public void Update(string name, AutomationTrigger trigger, IReadOnlyList<AutomationCondition>? conditions,
        IReadOnlyList<AutomationAction> actions, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Name must contain 1 to 200 characters.", nameof(name));
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        if (actions is null || actions.Count == 0) throw new ArgumentException("At least one action is required.", nameof(actions));
        foreach (var condition in conditions ?? []) condition.Validate();
        foreach (var action in actions) action.Validate();
        Name = name.Trim(); Trigger = trigger;
        Conditions = JsonSerializer.Serialize(conditions ?? [], Json);
        Actions = JsonSerializer.Serialize(actions, Json);
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
