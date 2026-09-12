using PropFlow.Domain.Assets;
using PropFlow.Domain.Integrations;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class MappingProfileTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Connection = Guid.NewGuid();
    private static readonly Guid Portfolio = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static MappingProfile Profile(IntegrationEntityKind kind) =>
        new(Org, Guid.NewGuid(), Connection, kind, T0);

    private static MappingRule Rule(MappingProfile profile, MappingSourceField field, string source, string target) =>
        new(Org, Guid.NewGuid(), profile.Id, field, source, target);

    private static MappingProfile WorkOrderProfile(params (string Source, string Target)[] statusRules)
    {
        var profile = Profile(IntegrationEntityKind.WorkOrder);
        profile.SetDefaults(null, Creator, null, T0);
        foreach (var (source, target) in statusRules)
            profile.AddRule(Rule(profile, MappingSourceField.WorkOrderStatus, source, target), T0);
        return profile;
    }

    // --- Mode ------------------------------------------------------------------------------

    [Fact]
    public void A_new_profile_is_report_only()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        Assert.Equal(MappingMode.ReportOnly, profile.Mode);
        Assert.False(profile.AppliesChanges);
    }

    [Fact]
    public void Promotion_is_refused_while_the_profile_has_errors()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        Assert.True(profile.HasErrors);
        Assert.Throws<InvalidOperationException>(() => profile.PromoteToAutoApply(T0));
        Assert.Equal(MappingMode.ReportOnly, profile.Mode);
    }

    [Fact]
    public void Promotion_succeeds_once_the_required_defaults_are_supplied()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);

        Assert.False(profile.HasErrors);
        profile.PromoteToAutoApply(T0.AddMinutes(1));

        Assert.Equal(MappingMode.AutoApply, profile.Mode);
        Assert.True(profile.AppliesChanges);
        Assert.Equal(T0.AddMinutes(1), profile.UpdatedAt);
    }

    [Fact]
    public void Unmapped_findings_alone_do_not_block_promotion()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);

        var issues = profile.Validate();
        Assert.All(issues, i => Assert.Equal(MappingIssueSeverity.Unmapped, i.Severity));
        Assert.NotEmpty(issues);
        profile.PromoteToAutoApply(T0);
        Assert.Equal(MappingMode.AutoApply, profile.Mode);
    }

    [Fact]
    public void Clearing_a_required_default_demotes_an_auto_apply_profile()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);
        profile.PromoteToAutoApply(T0);

        profile.SetDefaults(null, null, "America/Chicago", T0.AddHours(1));

        Assert.Equal(MappingMode.ReportOnly, profile.Mode);
    }

    // --- Validation ------------------------------------------------------------------------

    [Fact]
    public void A_property_profile_requires_a_portfolio_and_a_default_time_zone()
    {
        var issues = Profile(IntegrationEntityKind.Property).Validate();

        Assert.Contains(issues, i => i.Severity == MappingIssueSeverity.Error && i.Field == "TargetPortfolioId");
        Assert.Contains(issues, i => i.Severity == MappingIssueSeverity.Error && i.Field == "DefaultTimeZoneId");
    }

    [Fact]
    public void A_default_time_zone_that_is_not_iana_is_an_error()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "Central Standard Time", T0);

        Assert.Contains(profile.Validate(),
            i => i.Severity == MappingIssueSeverity.Error && i.Field == "DefaultTimeZoneId");
    }

    [Fact]
    public void The_four_address_fields_are_reported_as_unmapped_not_dropped()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);

        var unmapped = profile.Validate()
            .Where(i => i.Severity == MappingIssueSeverity.Unmapped)
            .Select(i => i.Field)
            .ToList();

        Assert.Equal(new[] { "AddressLine", "City", "Region", "PostalCode" }, unmapped);
    }

    [Fact]
    public void A_work_order_profile_requires_a_creator_and_at_least_one_status_rule()
    {
        var issues = Profile(IntegrationEntityKind.WorkOrder).Validate();

        Assert.Contains(issues, i => i.Severity == MappingIssueSeverity.Error && i.Field == "DefaultCreatorId");
        Assert.Contains(issues, i => i.Severity == MappingIssueSeverity.Error && i.Field == "WorkOrderStatus");
    }

    [Fact]
    public void An_occupancy_profile_reports_the_synthetic_resident_id_as_unmapped()
    {
        var issues = Profile(IntegrationEntityKind.Occupancy).Validate();

        Assert.Contains(issues, i => i.Severity == MappingIssueSeverity.Unmapped && i.Field == "ResidentExternalId");
        Assert.DoesNotContain(issues, i => i.Severity == MappingIssueSeverity.Error);
    }

    [Fact]
    public void A_rule_target_that_is_not_a_domain_enum_member_is_an_error()
    {
        var profile = WorkOrderProfile(("open", "Unicorn"));

        Assert.Contains(profile.Validate(),
            i => i.Severity == MappingIssueSeverity.Error && i.Reason.Contains("is not a WorkStatus"));
    }

    [Fact]
    public void A_numeric_rule_target_is_not_accepted_as_an_enum_member()
    {
        var profile = WorkOrderProfile(("open", "99"));

        Assert.Contains(profile.Validate(),
            i => i.Severity == MappingIssueSeverity.Error && i.Reason.Contains("is not a WorkStatus"));
    }

    [Fact]
    public void Draft_cannot_be_the_target_of_an_imported_status()
    {
        var profile = WorkOrderProfile(("open", "Draft"));

        Assert.Contains(profile.Validate(),
            i => i.Severity == MappingIssueSeverity.Error && i.Reason.Contains("pre-publish"));
    }

    [Fact]
    public void The_same_source_value_mapped_twice_is_ambiguous_and_an_error()
    {
        var profile = WorkOrderProfile(("Open", "New"), ("open", "InProgress"));

        Assert.Contains(profile.Validate(),
            i => i.Severity == MappingIssueSeverity.Error && i.Reason.Contains("ambiguous"));
    }

    [Fact]
    public void A_rule_for_another_kind_is_refused_at_the_profile()
    {
        var profile = Profile(IntegrationEntityKind.WorkOrder);
        var rule = Rule(profile, MappingSourceField.AssetKind, "hvac", "Hvac");

        Assert.Throws<ArgumentException>(() => profile.AddRule(rule, T0));
    }

    [Fact]
    public void A_rule_from_another_profile_or_organization_is_refused()
    {
        var profile = Profile(IntegrationEntityKind.WorkOrder);
        var foreignProfile = new MappingRule(Org, Guid.NewGuid(), Guid.NewGuid(),
            MappingSourceField.WorkOrderStatus, "open", "New");
        var foreignOrg = new MappingRule(Guid.NewGuid(), Guid.NewGuid(), profile.Id,
            MappingSourceField.WorkOrderStatus, "open", "New");

        Assert.Throws<ArgumentException>(() => profile.AddRule(foreignProfile, T0));
        Assert.Throws<ArgumentException>(() => profile.AddRule(foreignOrg, T0));
    }

    [Fact]
    public void A_rule_requires_a_profile_a_source_value_and_a_target_value()
    {
        Assert.Throws<ArgumentException>(() =>
            new MappingRule(Org, Guid.NewGuid(), Guid.Empty, MappingSourceField.WorkOrderStatus, "open", "New"));
        Assert.Throws<ArgumentException>(() =>
            new MappingRule(Org, Guid.NewGuid(), Guid.NewGuid(), MappingSourceField.WorkOrderStatus, "  ", "New"));
        Assert.Throws<ArgumentException>(() =>
            new MappingRule(Org, Guid.NewGuid(), Guid.NewGuid(), MappingSourceField.WorkOrderStatus, "open", " "));
    }

    // --- Value mapping: an unmapped value is a conflict, never a silent default ---------------

    [Fact]
    public void A_mapped_status_resolves_to_the_closed_domain_enum()
    {
        var profile = WorkOrderProfile(("Open", "New"), ("In Progress", "InProgress"), ("closed", "Completed"));

        Assert.Equal(WorkStatus.New, profile.MapWorkStatus("Open"));
        Assert.Equal(WorkStatus.InProgress, profile.MapWorkStatus("in progress"));
        Assert.Equal(WorkStatus.Completed, profile.MapWorkStatus("  CLOSED "));
    }

    [Fact]
    public void An_unmapped_status_returns_null_so_the_caller_raises_a_conflict()
    {
        var profile = WorkOrderProfile(("Open", "New"));

        // The whole point: no fallback, no WorkStatus.New, no "Other". Null is the conflict signal.
        Assert.Null(profile.MapWorkStatus("Awaiting Parts"));
        Assert.Null(profile.MapWorkStatus(""));
        Assert.Null(profile.MapWorkStatus(null));
    }

    [Fact]
    public void A_status_rule_pointing_at_a_nonexistent_member_resolves_to_null_rather_than_a_default()
    {
        var profile = WorkOrderProfile(("open", "Unicorn"));

        Assert.Null(profile.MapWorkStatus("open"));
    }

    [Fact]
    public void Mapping_a_status_on_the_wrong_kind_of_profile_is_a_programming_error()
    {
        var profile = Profile(IntegrationEntityKind.Property);

        Assert.Throws<InvalidOperationException>(() => profile.MapWorkStatus("open"));
    }

    [Fact]
    public void Asset_kind_maps_through_rules_and_returns_null_when_unmapped()
    {
        var profile = Profile(IntegrationEntityKind.Asset);
        profile.AddRule(Rule(profile, MappingSourceField.AssetKind, "FURNACE", "Hvac"), T0);

        Assert.Equal(AssetKind.Hvac, profile.MapAssetKind("furnace"));
        Assert.Null(profile.MapAssetKind("Dumbwaiter"));
        // Absent is not unmapped; the caller may use AssetKind.Other for a blank.
        Assert.Null(profile.MapAssetKind(null));
    }

    // --- Time zone resolution ----------------------------------------------------------------

    [Fact]
    public void A_time_zone_rule_wins_over_the_external_value_and_the_default()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);
        profile.AddRule(Rule(profile, MappingSourceField.PropertyTimeZone, "CST", "America/Denver"), T0);

        Assert.Equal("America/Denver", profile.ResolveTimeZone("CST"));
    }

    [Fact]
    public void An_external_value_that_already_looks_iana_is_taken_as_is()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);

        Assert.Equal("Europe/London", profile.ResolveTimeZone("Europe/London"));
    }

    [Fact]
    public void A_non_iana_external_value_with_no_rule_falls_back_to_the_default()
    {
        var profile = Profile(IntegrationEntityKind.Property);
        profile.SetDefaults(Portfolio, null, "America/Chicago", T0);

        Assert.Equal("America/Chicago", profile.ResolveTimeZone("Central Standard Time"));
        Assert.Equal("America/Chicago", profile.ResolveTimeZone(null));
    }

    [Fact]
    public void With_no_default_and_no_usable_external_value_the_time_zone_is_unresolvable()
    {
        var profile = Profile(IntegrationEntityKind.Property);

        Assert.Null(profile.ResolveTimeZone("Central Standard Time"));
    }

    [Fact]
    public void The_iana_shape_check_matches_the_property_invariant()
    {
        Assert.True(MappingProfile.LooksLikeIanaTimeZone("America/New_York"));
        Assert.False(MappingProfile.LooksLikeIanaTimeZone("UTC"));
        Assert.False(MappingProfile.LooksLikeIanaTimeZone("America/New York"));
        Assert.False(MappingProfile.LooksLikeIanaTimeZone(" "));
        Assert.False(MappingProfile.LooksLikeIanaTimeZone(null));
    }

    [Fact]
    public void Every_source_field_belongs_to_exactly_one_entity_kind()
    {
        Assert.Equal(new[] { MappingSourceField.WorkOrderStatus },
            MappingVocabulary.For(IntegrationEntityKind.WorkOrder));
        Assert.Equal(new[] { MappingSourceField.AssetKind },
            MappingVocabulary.For(IntegrationEntityKind.Asset));
        Assert.Equal(new[] { MappingSourceField.PropertyTimeZone },
            MappingVocabulary.For(IntegrationEntityKind.Property));
        Assert.Empty(MappingVocabulary.For(IntegrationEntityKind.Space));
    }
}
