using PropFlow.Application.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class TemplateRendererTests
{
    private readonly TemplateRenderer renderer = new();

    [Fact]
    public void Substitutes_placeholders_including_whitespace_variants()
    {
        var result = renderer.Render("Pest control is scheduled for {{ day }} at {{time}}.",
            new Dictionary<string, string> { ["day"] = "Friday", ["time"] = "9:00 AM" });

        Assert.Equal("Pest control is scheduled for Friday at 9:00 AM.", result);
    }

    [Fact]
    public void Leaves_text_without_placeholders_unchanged()
    {
        Assert.Equal("No tokens here.", renderer.Render("No tokens here.", new Dictionary<string, string>()));
    }

    [Fact]
    public void Throws_when_a_placeholder_has_no_supplied_value()
    {
        var exception = Assert.Throws<TemplateRenderException>(() =>
            renderer.Render("Hello {{ name }}", new Dictionary<string, string>()));
        Assert.Contains("name", exception.Message);
    }

    [Theory]
    [InlineData("Hello {{ 1nvalid }}")]
    [InlineData("Hello {{ }}")]
    [InlineData("Hello {{ a b }}")]
    public void Throws_on_a_malformed_placeholder(string template)
    {
        Assert.Throws<TemplateRenderException>(() =>
            renderer.Render(template, new Dictionary<string, string> { ["a"] = "x" }));
    }

    [Fact]
    public void Placeholders_returns_distinct_names()
    {
        var names = renderer.Placeholders("{{a}} {{ b }} {{a}} {{c.d}}");

        Assert.Equal(3, names.Count);
        Assert.Contains("a", names);
        Assert.Contains("b", names);
        Assert.Contains("c.d", names);
    }

    [Fact]
    public void Placeholders_reports_a_malformed_token()
    {
        Assert.Throws<TemplateRenderException>(() => renderer.Placeholders("{{ 1bad }}"));
    }

    [Fact]
    public void Rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!, new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => renderer.Render("x", null!));
        Assert.Throws<ArgumentNullException>(() => renderer.Placeholders(null!));
    }
}
