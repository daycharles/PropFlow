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

    [Fact]
    public void Never_exceeds_the_maximum_length_or_has_edge_separators()
    {
        var slug = OrganizationSlug.From(new string('x', 200) + " tail");

        Assert.True(slug.Length <= OrganizationSlug.MaxLength);
        Assert.False(slug.StartsWith('-'));
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => OrganizationSlug.From(null!));
    }
}
