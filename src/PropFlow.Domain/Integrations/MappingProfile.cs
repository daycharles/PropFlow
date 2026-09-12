using PropFlow.Domain.Assets;
using PropFlow.Domain.Work;

namespace PropFlow.Domain.Integrations;

// How much authority a profile has over the PropFlow rows it reconciles into.
//
// ReportOnly is the CONSTRUCTED DEFAULT and there is no overload that skips it. A reconciler that
// silently rewrites a tenant's property names, work statuses and time zones on first connect is a
// support incident, not a feature: the operator reviews the conflict queue first, then promotes.
// Promotion is an explicit transition that refuses while Validate() reports an error.
public enum MappingMode
{
    ReportOnly = 0,
    AutoApply = 1
}

// Error blocks promotion to AutoApply. Unmapped is informational: a canonical field that PropFlow
// has nowhere to put. It is reported rather than silently dropped so an operator can see what an
// import will and will not carry across.
public enum MappingIssueSeverity
{
    Error = 0,
    Unmapped = 1
}

// One finding from MappingProfile.Validate(). Field names the canonical or profile field the
// finding concerns; Reason is the sentence a human reads in the mapping panel.
public sealed record MappingIssue(MappingIssueSeverity Severity, string Field, string Reason);

// The closed vocabulary a rule may key on. This is deliberately NOT an expression language and
// NOT a JSON blob — the same call the repo already made for AutomationCondition/AutomationAction
// (AutomationRule.cs:6-8): administrators pick from these values, the database never becomes an
// executable DSL. A new source field is a new enum member plus a new arm in MappingProfile, which
// is exactly the review that a free-form rule column would skip.
public enum MappingSourceField
{
    // CanonicalWorkOrder.Status (a bare string) -> WorkStatus (a closed domain enum).
    WorkOrderStatus = 0,

    // CanonicalAsset.Kind (an optional string) -> AssetKind.
    AssetKind = 1,

    // CanonicalProperty.TimeZone (an optional, free-form label) -> an IANA zone id.
    PropertyTimeZone = 2
}

// Which entity kind each source field belongs to. A profile is per (connection, entity kind), so
// a rule whose field belongs to another kind is a programming error, not a configuration one.
public static class MappingVocabulary
{
    public static IntegrationEntityKind AppliesTo(MappingSourceField field) => field switch
    {
        MappingSourceField.WorkOrderStatus => IntegrationEntityKind.WorkOrder,
        MappingSourceField.AssetKind => IntegrationEntityKind.Asset,
        MappingSourceField.PropertyTimeZone => IntegrationEntityKind.Property,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    public static IReadOnlyList<MappingSourceField> For(IntegrationEntityKind kind) =>
        Enum.GetValues<MappingSourceField>().Where(f => AppliesTo(f) == kind).ToArray();
}

// One value translation: "this external value means that PropFlow value". SourceValue and
// TargetValue are plain strings because the source side is whatever the vendor emits and the
// target side is an enum member name or an IANA zone id; the *field* is what is closed.
public sealed class MappingRule : TenantEntity
{
    public const int SourceValueMaxLength = 200;
    public const int TargetValueMaxLength = 200;

    // EF materialization only.
    private MappingRule(Guid organizationId, Guid id) : base(organizationId, id) { }

    public MappingRule(Guid organizationId, Guid id, Guid profileId, MappingSourceField sourceField,
        string sourceValue, string targetValue) : base(organizationId, id)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Profile is required.", nameof(profileId));
        if (!Enum.IsDefined(sourceField)) throw new ArgumentOutOfRangeException(nameof(sourceField));
        ProfileId = profileId;
        SourceField = sourceField;
        SourceValue = IntegrationConnection.RequireSingleLine(sourceValue, nameof(sourceValue), SourceValueMaxLength);
        TargetValue = IntegrationConnection.RequireSingleLine(targetValue, nameof(targetValue), TargetValueMaxLength);
    }

    public Guid ProfileId { get; private set; }
    public MappingSourceField SourceField { get; private set; }
    public string SourceValue { get; private set; } = "";
    public string TargetValue { get; private set; } = "";

    public void Retarget(string targetValue) =>
        TargetValue = IntegrationConnection.RequireSingleLine(targetValue, nameof(targetValue), TargetValueMaxLength);

    // Matching is trim + case-insensitive and nothing else. External systems are inconsistent
    // about "Open" vs "open"; they are not inconsistent in ways a regex should paper over.
    public static string NormalizeSourceValue(string? value) => (value ?? "").Trim().ToLowerInvariant();

    public bool Matches(string? externalValue) =>
        NormalizeSourceValue(SourceValue) == NormalizeSourceValue(externalValue)
        && NormalizeSourceValue(externalValue).Length > 0;
}

// The per-(connection, entity kind) mapping configuration: the facts the canonical records cannot
// supply, plus the value rules that turn vendor strings into closed domain enums.
//
// Each default exists because a domain constructor demands something the canonical shape has not
// got. Verified against the tree:
//   TargetPortfolioId  - Property's constructor requires a non-empty portfolio id
//                        (Properties/PropertyHierarchy.cs:18,31) and CanonicalProperty has no
//                        portfolio field (CanonicalRecords.cs:9-16).
//   DefaultCreatorId   - WorkItem's constructor requires a non-empty creator id
//                        (Work/WorkItem.cs:10) and an external work order has no PropFlow user.
//   DefaultTimeZoneId  - Property.ValidateTimeZone rejects anything without a '/'
//                        (Properties/PropertyHierarchy.cs:32-38) and CanonicalProperty.TimeZone is
//                        optional and free-form.
//
// CanonicalProperty also carries AddressLine/City/Region/PostalCode (CanonicalRecords.cs:12-15) and
// NO PropFlow entity stores a postal address (verified 2026-09-12: the only "address" in the domain
// is OutboxMessage.RecipientAddress, a messaging address). Validate() therefore reports those four
// as Unmapped rather than dropping them silently. Adding address columns to Property is an FS-S02
// change and would drag a second operations migration into this story.
public sealed class MappingProfile : TenantEntity
{
    public const int TimeZoneMaxLength = 100;

    // The canonical property fields PropFlow has nowhere to put. Reported, never dropped silently.
    public static readonly IReadOnlyList<string> UnstorablePropertyFields =
        ["AddressLine", "City", "Region", "PostalCode"];

    private readonly List<MappingRule> rules = [];

    // EF materialization only.
    private MappingProfile(Guid organizationId, Guid id) : base(organizationId, id) { }

    public MappingProfile(Guid organizationId, Guid id, Guid connectionId, IntegrationEntityKind kind,
        DateTimeOffset createdAt) : base(organizationId, id)
    {
        if (connectionId == Guid.Empty) throw new ArgumentException("Connection is required.", nameof(connectionId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ConnectionId = connectionId;
        Kind = kind;
        Mode = MappingMode.ReportOnly;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid ConnectionId { get; private set; }
    public IntegrationEntityKind Kind { get; private set; }
    public MappingMode Mode { get; private set; } = MappingMode.ReportOnly;
    public Guid? TargetPortfolioId { get; private set; }
    public Guid? DefaultCreatorId { get; private set; }
    public string? DefaultTimeZoneId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<MappingRule> Rules => rules;

    // The reconciler asks this, not Mode, so the ReportOnly meaning lives in one place.
    public bool AppliesChanges => Mode == MappingMode.AutoApply;

    public void SetDefaults(Guid? targetPortfolioId, Guid? defaultCreatorId, string? defaultTimeZoneId,
        DateTimeOffset at)
    {
        TargetPortfolioId = targetPortfolioId is { } portfolio && portfolio != Guid.Empty ? portfolio : null;
        DefaultCreatorId = defaultCreatorId is { } creator && creator != Guid.Empty ? creator : null;
        DefaultTimeZoneId = string.IsNullOrWhiteSpace(defaultTimeZoneId)
            ? null
            : IntegrationConnection.RequireSingleLine(defaultTimeZoneId, nameof(defaultTimeZoneId), TimeZoneMaxLength);
        Touch(at);
        DemoteWhenInvalid(at);
    }

    public void AddRule(MappingRule rule, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.OrganizationId != OrganizationId)
            throw new ArgumentException("The rule belongs to another organization.", nameof(rule));
        if (rule.ProfileId != Id)
            throw new ArgumentException("The rule belongs to another mapping profile.", nameof(rule));
        if (MappingVocabulary.AppliesTo(rule.SourceField) != Kind)
            throw new ArgumentException(
                $"A {rule.SourceField} rule does not apply to a {Kind} profile.", nameof(rule));
        rules.Add(rule);
        Touch(at);
        DemoteWhenInvalid(at);
    }

    public bool RemoveRule(Guid ruleId, DateTimeOffset at)
    {
        var removed = rules.RemoveAll(r => r.Id == ruleId) > 0;
        if (!removed) return false;
        Touch(at);
        DemoteWhenInvalid(at);
        return true;
    }

    /// <summary>
    /// Every finding about this profile's configuration. PURE: no I/O, no database, no clock — it
    /// reads only the profile and its rules, so the mapping panel, the promotion guard and a unit
    /// test all get the same answer. Errors block promotion to AutoApply; Unmapped findings do not.
    /// </summary>
    public IReadOnlyList<MappingIssue> Validate()
    {
        var issues = new List<MappingIssue>();

        switch (Kind)
        {
            case IntegrationEntityKind.Property:
                if (TargetPortfolioId is null)
                    issues.Add(new MappingIssue(MappingIssueSeverity.Error, nameof(TargetPortfolioId),
                        "Property's constructor requires a portfolio and CanonicalProperty carries none, so the profile must name the portfolio imported properties land in."));
                if (DefaultTimeZoneId is null)
                    issues.Add(new MappingIssue(MappingIssueSeverity.Error, nameof(DefaultTimeZoneId),
                        "CanonicalProperty.TimeZone is optional and free-form while Property requires an IANA zone, so a fallback zone is required."));
                else if (!LooksLikeIanaTimeZone(DefaultTimeZoneId))
                    issues.Add(new MappingIssue(MappingIssueSeverity.Error, nameof(DefaultTimeZoneId),
                        $"'{DefaultTimeZoneId}' is not an IANA zone identifier; Property.ValidateTimeZone requires a '/' and no spaces."));
                foreach (var field in UnstorablePropertyFields)
                    issues.Add(new MappingIssue(MappingIssueSeverity.Unmapped, field,
                        "No PropFlow entity stores a postal address, so this canonical field is imported into nothing. Address columns on Property are an FS-S02 change."));
                break;

            case IntegrationEntityKind.WorkOrder:
                if (DefaultCreatorId is null)
                    issues.Add(new MappingIssue(MappingIssueSeverity.Error, nameof(DefaultCreatorId),
                        "WorkItem's constructor requires a creator and an external work order has no PropFlow user, so the profile must name the user imported work is attributed to."));
                if (!rules.Any(r => r.SourceField == MappingSourceField.WorkOrderStatus))
                    issues.Add(new MappingIssue(MappingIssueSeverity.Error, nameof(MappingSourceField.WorkOrderStatus),
                        "CanonicalWorkOrder.Status is a free string and WorkStatus is closed; with no status rule every incoming work order raises an unmapped-value conflict."));
                break;

            case IntegrationEntityKind.Asset:
                if (!rules.Any(r => r.SourceField == MappingSourceField.AssetKind))
                    issues.Add(new MappingIssue(MappingIssueSeverity.Unmapped, nameof(MappingSourceField.AssetKind),
                        "No asset-kind rules are configured. An absent CanonicalAsset.Kind imports as AssetKind.Other; a present but unmapped one raises a conflict."));
                break;

            case IntegrationEntityKind.Occupancy:
                issues.Add(new MappingIssue(MappingIssueSeverity.Unmapped, "ResidentExternalId",
                    "CanonicalOccupancy carries no resident external id, so residents are linked through the synthetic id from SyntheticExternalId.ForResident. A future CanonicalResident supersedes it without breaking existing links."));
                break;

            default:
                // Space, and the structural kinds added by PF-S19.03, need no profile-level fact
                // the canonical record has not already got.
                break;
        }

        // Ambiguity: the same external value mapped twice. First match would silently win, so this
        // is an error rather than a preference.
        foreach (var group in rules.GroupBy(r => (r.SourceField, Value: MappingRule.NormalizeSourceValue(r.SourceValue)))
                     .Where(g => g.Count() > 1))
            issues.Add(new MappingIssue(MappingIssueSeverity.Error, group.Key.SourceField.ToString(),
                $"'{group.Key.Value}' is mapped {group.Count()} times; the mapping is ambiguous."));

        foreach (var rule in rules)
            if (ValidateRule(rule) is { } issue)
                issues.Add(issue);

        return issues;
    }

    public bool HasErrors => Validate().Any(i => i.Severity == MappingIssueSeverity.Error);

    /// <summary>
    /// The one way into AutoApply. Refuses while Validate() reports an error, so a profile can
    /// never start rewriting tenant rows using a default it has not got.
    /// </summary>
    public void PromoteToAutoApply(DateTimeOffset at)
    {
        var errors = Validate().Where(i => i.Severity == MappingIssueSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"The mapping profile has {errors.Count} validation error(s) and cannot auto-apply. First: {errors[0].Reason}");
        Mode = MappingMode.AutoApply;
        Touch(at);
    }

    public void RevertToReportOnly(DateTimeOffset at)
    {
        Mode = MappingMode.ReportOnly;
        Touch(at);
    }

    /// <summary>
    /// The PropFlow status an external work-order status means. Null means NO rule matched, and the
    /// caller raises a ConflictReason.UnmappedValue conflict. There is deliberately no fallback:
    /// importing an unknown vendor status as New or Draft would fabricate work state that nobody
    /// asked for, and the operator would never learn the vocabulary had drifted.
    /// </summary>
    public WorkStatus? MapWorkStatus(string? externalStatus)
    {
        RequireKind(IntegrationEntityKind.WorkOrder);
        var mapped = Resolve<WorkStatus>(MappingSourceField.WorkOrderStatus, externalStatus);
        // Draft is PropFlow's pre-publish state; imported work is already real work.
        return mapped == WorkStatus.Draft ? null : mapped;
    }

    /// <summary>
    /// The asset kind an external kind string means. A null or blank CanonicalAsset.Kind is
    /// ABSENT, not unmapped — the caller may use AssetKind.Other for it. A non-blank value with no
    /// rule returns null and is a conflict.
    /// </summary>
    public AssetKind? MapAssetKind(string? externalKind)
    {
        RequireKind(IntegrationEntityKind.Asset);
        return Resolve<AssetKind>(MappingSourceField.AssetKind, externalKind);
    }

    /// <summary>
    /// The IANA zone an imported property gets. Precedence: an explicit PropertyTimeZone rule for
    /// the external value, then the external value itself when it already looks like an IANA id,
    /// then DefaultTimeZoneId. Null means the profile cannot supply one and the caller raises a
    /// ConflictReason.MissingRequiredMapping conflict.
    /// </summary>
    public string? ResolveTimeZone(string? externalTimeZone)
    {
        RequireKind(IntegrationEntityKind.Property);
        if (!string.IsNullOrWhiteSpace(externalTimeZone))
        {
            foreach (var rule in rules)
                if (rule.SourceField == MappingSourceField.PropertyTimeZone && rule.Matches(externalTimeZone))
                    return LooksLikeIanaTimeZone(rule.TargetValue) ? rule.TargetValue.Trim() : null;
            var trimmed = externalTimeZone.Trim();
            if (LooksLikeIanaTimeZone(trimmed)) return trimmed;
        }
        return DefaultTimeZoneId;
    }

    // Mirrors Property.ValidateTimeZone (Properties/PropertyHierarchy.cs:32-38), which is private:
    // 1 to 100 characters, at least one '/', no spaces. Duplicated on purpose so validation stays
    // pure — constructing a Property to find out would allocate an aggregate per candidate and pull
    // an unrelated invariant into the integrations path.
    public static bool LooksLikeIanaTimeZone(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length is > 0 and <= TimeZoneMaxLength
            && trimmed.Contains('/', StringComparison.Ordinal)
            && !trimmed.Contains(' ', StringComparison.Ordinal);
    }

    private MappingIssue? ValidateRule(MappingRule rule)
    {
        if (MappingVocabulary.AppliesTo(rule.SourceField) != Kind)
            return new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                $"A {rule.SourceField} rule does not apply to a {Kind} profile.");

        switch (rule.SourceField)
        {
            case MappingSourceField.WorkOrderStatus:
                if (!TryParseMember<WorkStatus>(rule.TargetValue, out var status))
                    return new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                        $"'{rule.TargetValue}' is not a WorkStatus, so '{rule.SourceValue}' maps to nothing.");
                if (status == WorkStatus.Draft)
                    return new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                        "Draft is PropFlow's pre-publish state and cannot be the target of an imported status.");
                return null;

            case MappingSourceField.AssetKind:
                return TryParseMember<AssetKind>(rule.TargetValue, out _)
                    ? null
                    : new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                        $"'{rule.TargetValue}' is not an AssetKind, so '{rule.SourceValue}' maps to nothing.");

            case MappingSourceField.PropertyTimeZone:
                return LooksLikeIanaTimeZone(rule.TargetValue)
                    ? null
                    : new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                        $"'{rule.TargetValue}' is not an IANA zone identifier.");

            default:
                return new MappingIssue(MappingIssueSeverity.Error, rule.SourceField.ToString(),
                    "Unknown source field.");
        }
    }

    private T? Resolve<T>(MappingSourceField field, string? externalValue) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(externalValue)) return null;
        foreach (var rule in rules)
        {
            if (rule.SourceField != field || !rule.Matches(externalValue)) continue;
            return TryParseMember<T>(rule.TargetValue, out var value) ? value : null;
        }
        return null;
    }

    // Enum.TryParse happily accepts "7" and any undefined numeric value, which would let a rule
    // target a member that does not exist. Enum.IsDefined closes that.
    private static bool TryParseMember<T>(string? value, out T parsed) where T : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    private void RequireKind(IntegrationEntityKind expected)
    {
        if (Kind != expected)
            throw new InvalidOperationException($"This is a {Kind} profile, not a {expected} profile.");
    }

    // An edit that invalidates the profile drops it back to ReportOnly rather than leaving an
    // AutoApply profile running on a default it no longer has. Silent, but in the safe direction:
    // the alternative is a reconciler that keeps writing with a missing portfolio id.
    private void DemoteWhenInvalid(DateTimeOffset at)
    {
        if (Mode == MappingMode.AutoApply && HasErrors) RevertToReportOnly(at);
    }

    private void Touch(DateTimeOffset at) => UpdatedAt = at.ToUniversalTime();
}
