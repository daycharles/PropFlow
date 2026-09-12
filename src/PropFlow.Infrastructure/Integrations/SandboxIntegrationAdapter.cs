using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PropFlow.Application.Integrations;

namespace PropFlow.Infrastructure.Integrations;

/// <summary>
/// The second adapter FS-S19 ships: a real HTTP client against a <b>PropFlow-defined</b>
/// JSON-over-HTTP contract, not a vendor's API. It exists so the adapter contract is proven over
/// a network boundary — auth, non-success statuses, malformed payloads, per-connection
/// credentials — which the in-memory <see cref="MockIntegrationAdapter"/> cannot exercise.
/// The full contract, including what a sandbox server must serve, is
/// <c>docs/integration-sandbox-contract.md</c>.
///
/// <para><b>Request.</b> <c>GET {Integrations:Sandbox:BaseAddress}/snapshot</c>, with
/// <c>Authorization: Bearer &lt;credential&gt;</c> where the credential is this connection's,
/// resolved from <see cref="IIntegrationSecretStore"/> by connection id. Accept is
/// <c>application/json</c>. No request body, no query string; this is a whole-source snapshot
/// and the descriptor says so (<see cref="IntegrationAdapterDescriptor.SupportsIncrementalPull"/>
/// is false).</para>
///
/// <para><b>Response.</b> 200 with a JSON object carrying five optional arrays —
/// <c>properties</c>, <c>spaces</c>, <c>occupancies</c>, <c>workOrders</c>, <c>assets</c>. An
/// omitted or null array means "none right now", not "unsupported": this adapter's descriptor
/// claims all five kinds, so a sandbox server that cannot produce a kind is declaring it empty.
/// Field names are camelCase and match the <c>Canonical*</c> record properties one-for-one;
/// matching is case-insensitive. Dates are ISO-8601 — <c>YYYY-MM-DD</c> for the
/// <see cref="DateOnly"/> fields, a full offset-bearing timestamp for the
/// <see cref="DateTimeOffset"/> ones. The response's own idea of its source system, if it sends
/// one, is ignored: PropFlow stamps <see cref="SourceSystem"/> itself.</para>
///
/// <para><b>Occupancy carries no resident id, and that is load-bearing.</b>
/// <c>CanonicalOccupancy</c> has resident name/email/phone but no resident external id, while
/// <c>Occupancy</c> requires a <c>ResidentId</c>. The reconciler (PF-S19.02/.03) closes that gap
/// with a synthetic resident link id of the form <c>"{occupancyExternalId}#resident"</c>, built
/// in one named place. Two consequences for anyone serving this contract: an occupancy's
/// <c>externalId</c> must be <b>stable for the life of that tenancy</b>, because a resident's
/// identity is derived from it; and one occupancy means one resident, so a joint tenancy is two
/// occupancy records today. A future <c>CanonicalResident</c> (or a <c>residentExternalId</c>
/// field on the occupancy payload) supersedes the rule additively — the reconciler prefers a real
/// id when one is present and falls back to the synthetic form — so links already written under
/// the synthetic id stay valid and are not rewritten.</para>
///
/// <para><b>Failure.</b> Every failure is an <see cref="IntegrationPullException"/> with a short,
/// PropFlow-authored, single-line message, which <c>EfIntegrationOperations.SyncAsync</c> records
/// as the connection's <c>LastError</c> and PF-S19.06 can treat as a provider outage. A provider
/// response body is never put in that message — matching
/// <c>RealMessageSenders</c>, a non-success status is reported as its status code alone.</para>
/// </summary>
public sealed class SandboxIntegrationAdapter(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IIntegrationSecretStore secrets) : IIntegrationAdapter
{
    public const string Source = "sandbox";
    public const string BaseAddressKey = "Integrations:Sandbox:BaseAddress";
    public const string SnapshotPath = "snapshot";

    public string SourceSystem => Source;
    public string DisplayName => "Sandbox property system (HTTP)";

    // Property management, all five canonical kinds, whole-source snapshot only. The sandbox
    // contract defines a payload for every kind, so this claim is honest; do not widen it to a
    // kind the contract has no array for.
    public IntegrationAdapterDescriptor Descriptor => IntegrationAdapterDescriptor.FullPropertyManagementSnapshot;

    public async Task<IntegrationSnapshot> PullAsync(IntegrationPullContext context, CancellationToken cancellationToken)
    {
        var baseAddress = configuration[BaseAddressKey];
        if (string.IsNullOrWhiteSpace(baseAddress) ||
            !Uri.TryCreate(baseAddress.TrimEnd('/') + "/" + SnapshotPath, UriKind.Absolute, out var endpoint))
            throw new IntegrationPullException(
                $"Set {BaseAddressKey} to the sandbox source's absolute base address before syncing this connection.");

        var secret = await secrets.ResolveAsync(context.ConnectionId, cancellationToken);
        if (secret.Outcome is IntegrationSecretOutcome.NotConfigured)
            throw new IntegrationPullException(
                $"No credential is configured for this connection. Set {ConfiguredIntegrationSecretStore.KeyFor(context.ConnectionId)}.");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Secret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // The transport message can name the host and the underlying socket error; neither
            // belongs on a health screen, and its shape is platform-dependent.
            throw new IntegrationPullException("The sandbox source could not be reached.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new IntegrationPullException($"The sandbox source returned {(int)response.StatusCode}.");

            SnapshotPayload? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<SnapshotPayload>(JsonSerializerOptions.Web, cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // NotSupportedException is what ReadFromJsonAsync raises for a non-JSON content
                // type — a 200 carrying an HTML error page, for instance.
                throw new IntegrationPullException("The sandbox source returned a malformed snapshot payload.");
            }
            if (payload is null)
                throw new IntegrationPullException("The sandbox source returned an empty snapshot payload.");

            return Map(payload);
        }
    }

    private static IntegrationSnapshot Map(SnapshotPayload payload) => new(
        Source,
        Select(payload.Properties, "properties", static (p, i) => new CanonicalProperty(
            Required(p.ExternalId, "properties", i, "externalId"),
            Required(p.Name, "properties", i, "name"),
            Optional(p.AddressLine), Optional(p.City), Optional(p.Region),
            Optional(p.PostalCode), Optional(p.TimeZone))),
        Select(payload.Spaces, "spaces", static (s, i) => new CanonicalSpace(
            Required(s.ExternalId, "spaces", i, "externalId"),
            Required(s.PropertyExternalId, "spaces", i, "propertyExternalId"),
            Required(s.Code, "spaces", i, "code"),
            Optional(s.BuildingExternalId))),
        Select(payload.Occupancies, "occupancies", static (o, i) => new CanonicalOccupancy(
            Required(o.ExternalId, "occupancies", i, "externalId"),
            Required(o.SpaceExternalId, "occupancies", i, "spaceExternalId"),
            Required(o.ResidentName, "occupancies", i, "residentName"),
            Optional(o.Email), Optional(o.Phone),
            RequiredDate(o.MovedInOn, "occupancies", i, "movedInOn"),
            o.MovedOutOn)),
        Select(payload.WorkOrders, "workOrders", static (w, i) => new CanonicalWorkOrder(
            Required(w.ExternalId, "workOrders", i, "externalId"),
            Required(w.PropertyExternalId, "workOrders", i, "propertyExternalId"),
            Optional(w.SpaceExternalId),
            Required(w.Title, "workOrders", i, "title"),
            Optional(w.Description),
            Required(w.Status, "workOrders", i, "status"),
            RequiredInstant(w.OpenedAt, "workOrders", i, "openedAt"),
            w.ClosedAt)),
        Select(payload.Assets, "assets", static (a, i) => new CanonicalAsset(
            Required(a.ExternalId, "assets", i, "externalId"),
            Required(a.PropertyExternalId, "assets", i, "propertyExternalId"),
            Optional(a.SpaceExternalId),
            Required(a.Name, "assets", i, "name"),
            Optional(a.Kind), Optional(a.Manufacturer), Optional(a.Model), Optional(a.SerialNumber),
            a.InstalledOn)));

    // A null array is "none", never an error — see the class remarks.
    private static IReadOnlyList<TOut> Select<TIn, TOut>(
        List<TIn>? source, string kind, Func<TIn, int, TOut> map) where TIn : class
    {
        if (source is null || source.Count == 0) return [];
        var mapped = new List<TOut>(source.Count);
        for (var i = 0; i < source.Count; i++)
            mapped.Add(source[i] is { } item
                ? map(item, i)
                : throw new IntegrationPullException($"The sandbox snapshot has a null entry at {kind}[{i}]."));
        return mapped;
    }

    // The canonical records declare non-nullable strings, but System.Text.Json does not enforce
    // nullable annotations — deserializing straight into them would produce a record with a null
    // ExternalId that blows up much later, in the reconciler. Validate at the boundary instead,
    // naming the exact field so an implementer of the contract can fix their payload.
    private static string Required(string? value, string kind, int index, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new IntegrationPullException($"The sandbox snapshot is missing required field '{field}' at {kind}[{index}].")
            : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly RequiredDate(DateOnly? value, string kind, int index, string field) =>
        value ?? throw new IntegrationPullException($"The sandbox snapshot is missing required field '{field}' at {kind}[{index}].");

    private static DateTimeOffset RequiredInstant(DateTimeOffset? value, string kind, int index, string field) =>
        value ?? throw new IntegrationPullException($"The sandbox snapshot is missing required field '{field}' at {kind}[{index}].");

    // The wire shape. Every property is nullable because the sandbox server is external and
    // untrusted; `Map` decides which are actually required. `JsonSerializerOptions.Web` supplies
    // camelCase naming and case-insensitive matching.
    private sealed record SnapshotPayload(
        List<PropertyPayload>? Properties,
        List<SpacePayload>? Spaces,
        List<OccupancyPayload>? Occupancies,
        List<WorkOrderPayload>? WorkOrders,
        List<AssetPayload>? Assets);

    private sealed record PropertyPayload(
        string? ExternalId, string? Name, string? AddressLine, string? City,
        string? Region, string? PostalCode, string? TimeZone);

    private sealed record SpacePayload(
        string? ExternalId, string? PropertyExternalId, string? Code, string? BuildingExternalId);

    private sealed record OccupancyPayload(
        string? ExternalId, string? SpaceExternalId, string? ResidentName, string? Email,
        string? Phone, DateOnly? MovedInOn, DateOnly? MovedOutOn);

    private sealed record WorkOrderPayload(
        string? ExternalId, string? PropertyExternalId, string? SpaceExternalId, string? Title,
        string? Description, string? Status, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt);

    private sealed record AssetPayload(
        string? ExternalId, string? PropertyExternalId, string? SpaceExternalId, string? Name,
        string? Kind, string? Manufacturer, string? Model, string? SerialNumber, DateOnly? InstalledOn);
}
