using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Search;

namespace PropFlow.Infrastructure.Persistence;

// Fuzzy search over the tenant-scoped operations tables. Every query runs through the EF query
// filter (and, underneath, RLS), so results never cross an organization. A case-insensitive
// substring match (index-backed by the gin_trgm_ops indexes) outranks a trigram-only match.
public sealed class EfGlobalSearch(OperationsStore store) : IGlobalSearch
{
    // The fuzzy-match floor. `SET LOCAL` takes no parameter, so it is a literal; the `q <% col`
    // filter (TrigramsAreWordSimilar) then means word_similarity(q, col) >= this value.
    private const string ThresholdSql = "SET LOCAL pg_trgm.word_similarity_threshold = 0.25";
    private const string Escape = "\\";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var q = term.Trim();
        var like = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var cap = Math.Clamp(limit, 1, 50);
        var hits = new List<SearchHit>();

        // One transaction so `SET LOCAL` scopes to this search: it drops pg_trgm's word-similarity
        // threshold to ours, which lets the fuzzy `q <% col` filter (TrigramsAreWordSimilar) ride
        // the gin_trgm_ops indexes instead of a per-row word_similarity() over a seq scan.
        await using var tx = await store.Database.BeginTransactionAsync(cancellationToken);
        await store.Database.ExecuteSqlRawAsync(ThresholdSql, cancellationToken);

        void Collect(string type, IEnumerable<Row> rows) =>
            hits.AddRange(rows.Select(r => new SearchHit(type, r.Id, r.Label, r.Sublabel, r.Has ? 1d + r.Sim : r.Sim)));

        Collect("Property", await store.Properties.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Name, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Name))
            .OrderByDescending(x => EF.Functions.ILike(x.Name, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Name))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Name, null, EF.Functions.TrigramsWordSimilarity(q, x.Name), EF.Functions.ILike(x.Name, like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Building", await store.Buildings.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Name, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Name))
            .OrderByDescending(x => EF.Functions.ILike(x.Name, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Name))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Name, null, EF.Functions.TrigramsWordSimilarity(q, x.Name), EF.Functions.ILike(x.Name, like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Space", await store.Spaces.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Code, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Code))
            .OrderByDescending(x => EF.Functions.ILike(x.Code, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Code))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Code, null, EF.Functions.TrigramsWordSimilarity(q, x.Code), EF.Functions.ILike(x.Code, like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Resident", await store.Residents.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.FullName, like, Escape)
                || EF.Functions.ILike(x.Email ?? "", like, Escape)
                || EF.Functions.ILike(x.Phone ?? "", like, Escape)
                || EF.Functions.TrigramsAreWordSimilar(q, x.FullName))
            .OrderByDescending(x => EF.Functions.ILike(x.FullName, like, Escape)
                || EF.Functions.ILike(x.Email ?? "", like, Escape)
                || EF.Functions.ILike(x.Phone ?? "", like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.FullName))
            .Take(cap)
            .Select(x => new Row(x.Id, x.FullName, x.Email ?? x.Phone,
                EF.Functions.TrigramsWordSimilarity(q, x.FullName),
                EF.Functions.ILike(x.FullName, like, Escape)
                    || EF.Functions.ILike(x.Email ?? "", like, Escape)
                    || EF.Functions.ILike(x.Phone ?? "", like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Vendor", await store.Vendors.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Name, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Name))
            .OrderByDescending(x => EF.Functions.ILike(x.Name, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Name))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Name, null, EF.Functions.TrigramsWordSimilarity(q, x.Name), EF.Functions.ILike(x.Name, like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Employee", await store.Employees.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.DisplayName, like, Escape)
                || EF.Functions.ILike(x.Email ?? "", like, Escape)
                || EF.Functions.TrigramsAreWordSimilar(q, x.DisplayName))
            .OrderByDescending(x => EF.Functions.ILike(x.DisplayName, like, Escape)
                || EF.Functions.ILike(x.Email ?? "", like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.DisplayName))
            .Take(cap)
            .Select(x => new Row(x.Id, x.DisplayName, x.Email,
                EF.Functions.TrigramsWordSimilarity(q, x.DisplayName),
                EF.Functions.ILike(x.DisplayName, like, Escape) || EF.Functions.ILike(x.Email ?? "", like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Category", await store.Categories.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Name, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Name))
            .OrderByDescending(x => EF.Functions.ILike(x.Name, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Name))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Name, null, EF.Functions.TrigramsWordSimilarity(q, x.Name), EF.Functions.ILike(x.Name, like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Asset", await store.Assets.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Name, like, Escape)
                || EF.Functions.ILike(x.SerialNumber ?? "", like, Escape)
                || EF.Functions.ILike(x.Model ?? "", like, Escape)
                || EF.Functions.TrigramsAreWordSimilar(q, x.Name))
            .OrderByDescending(x => EF.Functions.ILike(x.Name, like, Escape)
                || EF.Functions.ILike(x.SerialNumber ?? "", like, Escape)
                || EF.Functions.ILike(x.Model ?? "", like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Name))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Name, x.SerialNumber,
                EF.Functions.TrigramsWordSimilarity(q, x.Name),
                EF.Functions.ILike(x.Name, like, Escape)
                    || EF.Functions.ILike(x.SerialNumber ?? "", like, Escape)
                    || EF.Functions.ILike(x.Model ?? "", like, Escape)))
            .ToListAsync(cancellationToken));

        Collect("Work", await store.WorkItems.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Title, like, Escape) || EF.Functions.TrigramsAreWordSimilar(q, x.Title))
            .OrderByDescending(x => EF.Functions.ILike(x.Title, like, Escape))
            .ThenByDescending(x => EF.Functions.TrigramsWordSimilarity(q, x.Title))
            .Take(cap)
            .Select(x => new Row(x.Id, x.Title, null, EF.Functions.TrigramsWordSimilarity(q, x.Title), EF.Functions.ILike(x.Title, like, Escape)))
            .ToListAsync(cancellationToken));

        await tx.CommitAsync(cancellationToken);
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Label).ThenBy(h => h.Type).Take(cap).ToList();
    }

    private sealed record Row(Guid Id, string Label, string? Sublabel, double Sim, bool Has);
}
