using PropFlow.Domain.Configuration;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class NotificationPreferenceTests
{
    private static NotificationPreference Create(bool enabled = true) =>
        NotificationPreference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationEventType.WorkAssigned, enabled);

    [Fact]
    public void A_new_preference_carries_the_value_it_was_created_with()
    {
        var enabled = Create(enabled: true);
        Assert.True(enabled.Enabled);
        var disabled = Create(enabled: false);
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public void An_empty_user_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationPreference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, NotificationEventType.WorkAssigned, true));
    }

    [Fact]
    public void Toggling_flips_the_stored_value()
    {
        var preference = Create(enabled: true);
        preference.SetEnabled(false);
        Assert.False(preference.Enabled);
        preference.SetEnabled(true);
        Assert.True(preference.Enabled);
    }

    [Theory]
    [InlineData(NotificationEventType.WorkAssigned)]
    [InlineData(NotificationEventType.AutomationApplied)]
    [InlineData(NotificationEventType.InvitationReceived)]
    public void Every_defined_event_type_can_be_created(NotificationEventType eventType)
    {
        var preference = NotificationPreference.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), eventType, true);
        Assert.Equal(eventType, preference.EventType);
    }
}
