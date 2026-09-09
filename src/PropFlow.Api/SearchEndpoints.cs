using PropFlow.Application;
using PropFlow.Application.Search;

namespace PropFlow.Api;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (string? q, int? limit, IGlobalSearch search, CancellationToken ct) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length is < 2 or > 100)
                return Results.Problem(statusCode: 400, title: "Search term must contain 2 to 100 characters");
            return Results.Ok(await search.SearchAsync(term, limit ?? 20, ct));
        }).RequireAuthorization(Capabilities.ReadWork);
    }
}
