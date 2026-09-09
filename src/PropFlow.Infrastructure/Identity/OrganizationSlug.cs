using System.Text;

namespace PropFlow.Infrastructure.Identity;

// Normalizes an organization name into the lowercase kebab-case slug used for login and,
// later, tenant-scoped web routes. Login lookup lowercases and trims the supplied value, so a
// stored slug is always lowercase with no leading, trailing or repeated separators.
public static class OrganizationSlug
{
    public const int MaxLength = 63;

    public static string From(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length);
        var pendingSeparator = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && builder.Length > 0) builder.Append('-');
                pendingSeparator = false;
                builder.Append(ch);
            }
            else
            {
                pendingSeparator = true;
            }

            if (builder.Length >= MaxLength) break;
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "organization" : slug;
    }
}
