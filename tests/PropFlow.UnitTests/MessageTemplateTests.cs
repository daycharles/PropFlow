using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class MessageTemplateTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static MessageTemplate Email() =>
        new(Org, Guid.NewGuid(), "Completion", MessageChannel.Email, "Work complete", "The work is complete.");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new MessageTemplate(Org, Guid.NewGuid(), name, MessageChannel.Sms, null, "Body"));
    }

    [Fact]
    public void Rejects_a_name_over_200_characters()
    {
        Assert.Throws<ArgumentException>(() =>
            new MessageTemplate(Org, Guid.NewGuid(), new string('a', 201), MessageChannel.Sms, null, "Body"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Email_requires_a_subject(string? subject)
    {
        Assert.Throws<ArgumentException>(() =>
            new MessageTemplate(Org, Guid.NewGuid(), "T", MessageChannel.Email, subject, "Body"));
    }

    [Fact]
    public void Sms_rejects_a_subject()
    {
        Assert.Throws<ArgumentException>(() =>
            new MessageTemplate(Org, Guid.NewGuid(), "T", MessageChannel.Sms, "Subject", "Body"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_body(string body)
    {
        Assert.Throws<ArgumentException>(() =>
            new MessageTemplate(Org, Guid.NewGuid(), "T", MessageChannel.Sms, null, body));
    }

    [Fact]
    public void Trims_and_stores_content()
    {
        var template = new MessageTemplate(Org, Guid.NewGuid(), "  Named  ", MessageChannel.Email, "  Subj  ", "  Body  ");

        Assert.Equal("Named", template.Name);
        Assert.Equal("Subj", template.Subject);
        Assert.Equal("Body", template.Body);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void Revise_revalidates_against_the_fixed_channel()
    {
        var template = Email();

        Assert.Throws<ArgumentException>(() => template.Revise(null, "Updated"));

        template.Revise("New subject", "Updated body");
        Assert.Equal("New subject", template.Subject);
        Assert.Equal("Updated body", template.Body);
    }

    [Fact]
    public void Activate_and_deactivate_toggle_the_flag()
    {
        var template = Email();

        template.Deactivate();
        Assert.False(template.IsActive);

        template.Activate();
        Assert.True(template.IsActive);
    }
}
