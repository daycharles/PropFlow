namespace PropFlow.Application.Search;

public sealed record SearchHit(string Type, Guid Id, string Label, string? Sublabel, double Score);

public interface IGlobalSearch
{
    // Fuzzy, tenant-scoped search across the property, people, category, asset and work
    // records. Ordered by match score, best first.
    Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken);
}
