using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class MessageTextTests
{
    private static readonly string Cr = ((char)13).ToString();
    private static readonly string Lf = ((char)10).ToString();
    private static readonly string Tab = ((char)9).ToString();
    private static readonly string Bell = ((char)7).ToString();
    private static readonly string Nul = ((char)0).ToString();
    private static readonly string VerticalTab = ((char)11).ToString();

    [Fact]
    public void Single_line_fields_reject_carriage_return_and_line_feed()
    {
        Assert.Throws<ArgumentException>(() =>
            MessageText.RequireSingleLine("Repair scheduled" + Cr + Lf + "Bcc: attacker@evil.test", "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine("a" + Lf + "b", "field", 200));
    }

    [Fact]
    public void Single_line_fields_reject_tabs_and_other_control_characters()
    {
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine("tab" + Tab + "here", "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine("bell" + Bell + "here", "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine("null" + Nul + "byte", "field", 200));
    }

    [Fact]
    public void Single_line_fields_trim_and_enforce_length()
    {
        Assert.Equal("Visit scheduled", MessageText.RequireSingleLine("  Visit scheduled  ", "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine("   ", "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine(new string('x', 201), "field", 200));
        Assert.Throws<ArgumentException>(() => MessageText.RequireSingleLine(null, "field", 200));
    }

    [Fact]
    public void Bodies_allow_newlines_and_tabs_but_no_other_control_characters()
    {
        var body = "line one" + Lf + "line two" + Tab + "tail";
        Assert.Equal(body, MessageText.RequireBody(body, "body", 2000));

        Assert.Throws<ArgumentException>(() => MessageText.RequireBody("bell" + Bell + "here", "body", 2000));
        Assert.Throws<ArgumentException>(() => MessageText.RequireBody("vertical" + VerticalTab + "tab", "body", 2000));
        Assert.Throws<ArgumentException>(() => MessageText.RequireBody("   ", "body", 2000));
    }
}
