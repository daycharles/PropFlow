using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class OrganizationSlugTests
{
    [Theory]
    [InlineData("Harbor Management", "harbor-management")]
    [InlineData("  Tidewater Residential Management  ", "tidewater-residential-management")]
    [InlineData("A&B Property Co.", "a-b-property-co")]
    [InlineData("Norfolk---Residential", "norfolk-residential")]
    [InlineData("2024 Portfolio", "2024-portfolio")]
    public void Produces_a_lowercase_kebab_slug(string name, string expected)
    {
        Assert.Equal(expected, OrganizationSlug.From(name));
    }

    [Fact]
    public void Falls_back_when_the_name_has_no_slug_characters()
    {
        Assert.Equal("organization", OrganizationSlug.From("!!! ---"));
    }

    [Theory]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx tail")]
    // A separator lands exactly at the length boundary (62 chars, space, then more letters):
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbb")]
    [InlineData("word one two three four five six seven eight nine ten eleven twelve thirteen")]
    public void Never_exceeds_the_maximum_length_or_has_edge_separators(string name)
    {
        var slug = OrganizationSlug.From(name);

        Assert.True(slug.Length <= OrganizationSlug.MaxLength, $"slug was {slug.Length} chars: '{slug}'");
        Assert.False(slug.StartsWith('-'));
        Assert.False(slug.EndsWith('-'));
        Assert.DoesNotContain("--", slug);
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => OrganizationSlug.From(null!));
    }
}
