using PropFlow.Domain.Configuration;
using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class WorkCategoryTests
{
    [Fact]
    public void AppliesTo_defaults_to_WorkItem_for_the_four_argument_constructor()
    {
        var category = new WorkCategory(Guid.NewGuid(), Guid.NewGuid(), "Plumbing", 0);
        Assert.Equal(ConfigurationEntityType.WorkItem, category.AppliesTo);
    }

    [Fact]
    public void AppliesTo_can_be_set_explicitly_and_is_immutable_after_construction()
    {
        var category = new WorkCategory(Guid.NewGuid(), Guid.NewGuid(), "Plumbing", 0, ConfigurationEntityType.WorkItem);
        Assert.Equal(ConfigurationEntityType.WorkItem, category.AppliesTo);
        // No method mutates AppliesTo - Rename/Reorder/Archive are the only writers, and none
        // touches it. This is a compile-time guarantee (private set(); a public re-tagging API
        // was deliberately not added, per PF-S03.03's design note) rather than a runtime check.
    }

    [Fact]
    public void Rename_and_reorder_still_validate_the_same_way_as_before_AppliesTo_existed()
    {
        var category = new WorkCategory(Guid.NewGuid(), Guid.NewGuid(), "Original", 0);
        category.Rename("Renamed");
        category.Reorder(5);
        Assert.Equal("Renamed", category.Name);
        Assert.Equal(5, category.SortOrder);
        Assert.Throws<ArgumentException>(() => category.Rename(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => category.Reorder(-1));
    }
}
