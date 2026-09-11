using PropFlow.Application;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// PF-S01.05: team CRUD and membership. Identity.ManageMembers, same gate as invitations and the
// role matrix - teams are an organization-membership control-plane concept, not tenant "People"
// data (which is Capabilities.ManagePeople / residents).
public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organizations/current/teams").RequireAuthorization(Capabilities.ManageMembers);

        group.MapGet("/", async (ITenantContext tenant, TeamService teams, CancellationToken ct) =>
            Results.Ok(await teams.ListAsync(tenant.OrganizationId, ct)));

        group.MapPost("/", async (TeamRequest request, ITenantContext tenant, TeamService teams, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
                return Results.Problem(statusCode: 400, title: "Name must be 1-200 characters");
            try
            {
                var team = await teams.CreateAsync(tenant.OrganizationId, request.Name, ct);
                return Results.Created($"/api/organizations/current/teams/{team.Id}", team);
            }
            catch (InvalidOperationException ex) { return Results.Problem(statusCode: 409, title: ex.Message); }
        });

        group.MapGet("/{teamId:guid}/members", async (Guid teamId, ITenantContext tenant, TeamService teams, CancellationToken ct) =>
            Results.Ok(await teams.ListMemberIdsAsync(tenant.OrganizationId, teamId, ct)));

        group.MapPost("/{teamId:guid}/members/{userId:guid}", async (Guid teamId, Guid userId,
            ITenantContext tenant, TeamService teams, CancellationToken ct) =>
        {
            var outcome = await teams.AddMemberAsync(tenant.OrganizationId, teamId, userId, ct);
            return outcome switch
            {
                TeamService.MembershipOutcome.Applied => Results.NoContent(),
                TeamService.MembershipOutcome.TeamNotFound => Results.NotFound(),
                TeamService.MembershipOutcome.MembershipNotFound => Results.Problem(statusCode: 400,
                    title: "The user has no active membership in this organization"),
                _ => Results.Problem(statusCode: 500, title: "Unexpected outcome")
            };
        });

        group.MapDelete("/{teamId:guid}/members/{userId:guid}", async (Guid teamId, Guid userId,
            ITenantContext tenant, TeamService teams, CancellationToken ct) =>
        {
            await teams.RemoveMemberAsync(tenant.OrganizationId, teamId, userId, ct);
            return Results.NoContent();
        });
    }
}

public sealed record TeamRequest(string Name);
