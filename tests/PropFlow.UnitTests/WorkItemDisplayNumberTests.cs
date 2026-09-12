using PropFlow.Domain.Work;
using Xunit;

namespace PropFlow.UnitTests;

// PF-S03.04. WorkItem has no other dedicated unit test file today (its domain logic is mostly
// exercised through integration tests) - this one is scoped to just the new property.
public sealed class WorkItemDisplayNumberTests
{
    private static WorkItem Create() => new(Guid.NewGuid(), Guid.NewGuid(), "Leak in unit 4", Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void A_new_work_item_has_no_display_number()
    {
        Assert.Null(Create().DisplayNumber);
    }

    [Fact]
    public void SetDisplayNumber_assigns_it_once()
    {
        var work = Create();
        work.SetDisplayNumber("WO-00042");
        Assert.Equal("WO-00042", work.DisplayNumber);
    }

    [Fact]
    public void SetDisplayNumber_cannot_be_called_a_second_time()
    {
        var work = Create();
        work.SetDisplayNumber("WO-00042");
        Assert.Throws<InvalidOperationException>(() => work.SetDisplayNumber("WO-00043"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDisplayNumber_rejects_a_blank_value(string value)
    {
        Assert.Throws<ArgumentException>(() => Create().SetDisplayNumber(value));
    }

    [Fact]
    public void SetDisplayNumber_rejects_a_value_over_fifty_characters()
    {
        Assert.Throws<ArgumentException>(() => Create().SetDisplayNumber(new string('x', 51)));
    }
}
