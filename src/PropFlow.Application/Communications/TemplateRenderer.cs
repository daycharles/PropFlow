using System.Text.RegularExpressions;

namespace PropFlow.Application.Communications;

public sealed class TemplateRenderException : Exception
{
    public TemplateRenderException(string message) : base(message) { }
}

public interface ITemplateRenderer
{
    // Substitutes every {{ placeholder }} with its supplied value. A malformed token or a
    // placeholder with no supplied value throws rather than sending a half-filled message.
    string Render(string template, IReadOnlyDictionary<string, string> values);

    // The distinct placeholder names referenced by the template, for validating a saved
    // template against the set of variables the system knows how to supply.
    IReadOnlyCollection<string> Placeholders(string template);
}

public sealed partial class TemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return TokenPattern().Replace(template, match =>
        {
            var name = NameOrThrow(match.Value, match.Groups["body"].Value);
            if (!values.TryGetValue(name, out var value))
                throw new TemplateRenderException($"No value supplied for placeholder '{name}'.");
            return value;
        });
    }

    public IReadOnlyCollection<string> Placeholders(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TokenPattern().Matches(template))
            names.Add(NameOrThrow(match.Value, match.Groups["body"].Value));
        return names;
    }

    private static string NameOrThrow(string token, string body)
    {
        var name = NamePattern().Match(body);
        if (!name.Success)
            throw new TemplateRenderException($"Malformed placeholder '{token}'.");
        return name.Groups["name"].Value;
    }

    [GeneratedRegex(@"\{\{(?<body>.*?)\}\}", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z][A-Za-z0-9_.]*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
