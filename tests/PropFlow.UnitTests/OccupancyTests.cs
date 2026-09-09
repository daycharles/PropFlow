using PropFlow.Domain.People;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OccupancyTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateOnly MovedIn = new(2026, 1, 15);

    private static Occupancy New() => new(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MovedIn);

    [Fact]
    public void Requires_a_resident_and_a_space()
    {
        Assert.Throws<ArgumentException>(() => new Occupancy(Org, Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), MovedIn));
        Assert.Throws<ArgumentException>(() => new Occupancy(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, MovedIn));
    }

    [Fact]
    public void Is_current_until_ended()
    {
        var occupancy = New();
        Assert.True(occupancy.IsCurrent);
        Assert.Null(occupancy.MovedOutOn);

        occupancy.EndOn(MovedIn.AddMonths(6));
        Assert.False(occupancy.IsCurrent);
        Assert.Equal(MovedIn.AddMonths(6), occupancy.MovedOutOn);
    }

    [Fact]
    public void Move_out_cannot_precede_move_in()
    {
        var occupancy = New();
        Assert.Throws<ArgumentException>(() => occupancy.EndOn(MovedIn.AddDays(-1)));
    }

    [Fact]
    public void Move_out_may_equal_move_in()
    {
        var occupancy = New();
        occupancy.EndOn(MovedIn);
        Assert.Equal(MovedIn, occupancy.MovedOutOn);
    }
}
