using PropFlow.Domain.Accounting;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OwnerAccountingTests
{
    [Fact]
    public void Budget_requires_submission_before_approval_and_records_actor()
    {
        var budget = new Budget(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2026, "Operating plan", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => budget.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow));
        budget.Submit();
        var actor = Guid.NewGuid(); budget.Approve(actor, DateTimeOffset.UtcNow);
        Assert.Equal(BudgetStatus.Approved, budget.Status); Assert.Equal(actor, budget.ApprovedBy);
    }

    [Theory]
    [InlineData(50, 1000, 500)]
    [InlineData(25, 1200, 300)]
    public void Ownership_percentage_is_validated_and_scales_owner_amount(decimal percentage, decimal gross, decimal expected)
    {
        var ownership = new PropertyOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), percentage);
        Assert.Equal(expected, gross * ownership.Percentage / 100m);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PropertyOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void Owner_statement_exposes_reproducible_net_amount()
    {
        var statement = new OwnerStatement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 1, 1), new(2026, 1, 31), 1000, 400, 50, 100, "ABC", DateTimeOffset.UtcNow);
        Assert.Equal(450, statement.NetOwnerAmount);
        Assert.Equal("ABC", statement.SourceHash);
    }
}
