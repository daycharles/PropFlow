using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Integrations;
using Xunit;

namespace PropFlow.UnitTests;

// The sandbox adapter and the configuration-backed secret store (PF-S19.07). The HTTP calls go
// through a stub HttpMessageHandler — no socket is opened.
public sealed class SandboxIntegrationAdapterTests
{
    private static readonly Guid ConnectionId = Guid.Parse("2c9f8a41-7b3e-4d52-9a10-6f4c1e8b7d33");
    private static readonly IntegrationPullContext Context = new(ConnectionId);

    private const string BaseAddress = "https://sandbox.example.test/propflow";

    private const string FullSnapshot = """
        {
          "properties": [
            { "externalId": "SB-PROP-1", "name": "Willow Park", "addressLine": "9 Willow Rd",
              "city": "Norfolk", "region": "VA", "postalCode": "23504", "timeZone": "America/New_York" }
          ],
          "spaces": [
            { "externalId": "SB-SPACE-1", "propertyExternalId": "SB-PROP-1", "code": "201",
              "buildingExternalId": "SB-BLDG-1" }
          ],
          "occupancies": [
            { "externalId": "SB-OCC-1", "spaceExternalId": "SB-SPACE-1", "residentName": "Dana Reyes",
              "email": "dana.reyes@example.test", "phone": null, "movedInOn": "2025-07-01", "movedOutOn": null }
          ],
          "workOrders": [
            { "externalId": "SB-WO-1", "propertyExternalId": "SB-PROP-1", "spaceExternalId": "SB-SPACE-1",
              "title": "Broken blind", "description": "   ", "status": "Open",
              "openedAt": "2026-09-01T13:45:00+00:00", "closedAt": null }
          ],
          "assets": [
            { "externalId": "SB-ASSET-1", "propertyExternalId": "SB-PROP-1", "spaceExternalId": null,
              "name": "Boiler", "kind": "Boiler", "manufacturer": "Weil-McLain", "model": "CGa",
              "serialNumber": "WM-7781", "installedOn": "2020-02-14" }
          ]
        }
        """;

    [Fact]
    public async Task Pull_maps_a_full_snapshot_onto_the_canonical_records()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, FullSnapshot);
        var snapshot = await Adapter(handler).PullAsync(Context, default);

        Assert.Equal(SandboxIntegrationAdapter.Source, snapshot.SourceSystem);

        var property = Assert.Single(snapshot.Properties);
        Assert.Equal("SB-PROP-1", property.ExternalId);
        Assert.Equal("Willow Park", property.Name);
        Assert.Equal("America/New_York", property.TimeZone);

        var space = Assert.Single(snapshot.Spaces);
        Assert.Equal("SB-BLDG-1", space.BuildingExternalId);

        var occupancy = Assert.Single(snapshot.Occupancies);
        Assert.Equal(new DateOnly(2025, 7, 1), occupancy.MovedInOn);
        Assert.Null(occupancy.MovedOutOn);
        Assert.Null(occupancy.Phone);

        var workOrder = Assert.Single(snapshot.WorkOrders);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 13, 45, 0, TimeSpan.Zero), workOrder.OpenedAt);
        // A whitespace-only optional field is normalized to null rather than kept as "   ".
        Assert.Null(workOrder.Description);

        var asset = Assert.Single(snapshot.Assets);
        Assert.Equal(new DateOnly(2020, 2, 14), asset.InstalledOn);
        Assert.Null(asset.SpaceExternalId);
    }

    [Fact]
    public async Task Pull_requests_the_snapshot_path_with_the_connections_bearer_credential()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, """{"properties":[]}""");
        await Adapter(handler).PullAsync(Context, default);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://sandbox.example.test/propflow/snapshot", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sandbox-secret-for-this-connection", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Pull_treats_omitted_arrays_as_empty()
    {
        var snapshot = await Adapter(StubHandler.Returning(HttpStatusCode.OK, "{}")).PullAsync(Context, default);

        Assert.Empty(snapshot.Properties);
        Assert.Empty(snapshot.Spaces);
        Assert.Empty(snapshot.Occupancies);
        Assert.Empty(snapshot.WorkOrders);
        Assert.Empty(snapshot.Assets);
    }

    [Fact]
    public async Task Pull_fails_when_the_connection_has_no_credential()
    {
        var adapter = new SandboxIntegrationAdapter(
            new StubClientFactory(StubHandler.Returning(HttpStatusCode.OK, "{}")),
            Configuration(baseAddress: BaseAddress, secret: null),
            new ConfiguredIntegrationSecretStore(Configuration(baseAddress: BaseAddress, secret: null)));

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => adapter.PullAsync(Context, default));
        Assert.Contains(ConnectionId.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pull_fails_when_no_base_address_is_configured()
    {
        var adapter = new SandboxIntegrationAdapter(
            new StubClientFactory(StubHandler.Returning(HttpStatusCode.OK, "{}")),
            Configuration(baseAddress: null, secret: "s"),
            new ConfiguredIntegrationSecretStore(Configuration(baseAddress: null, secret: "s")));

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => adapter.PullAsync(Context, default));
        Assert.Contains(SandboxIntegrationAdapter.BaseAddressKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_reports_a_non_success_status_without_the_response_body()
    {
        var handler = StubHandler.Returning(HttpStatusCode.Unauthorized, """{"error":"bad token sk-live-1234"}""");

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => Adapter(handler).PullAsync(Context, default));
        Assert.Equal("The sandbox source returned 401.", exception.Message);
        Assert.DoesNotContain("sk-live-1234", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_reports_a_malformed_payload()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, "{ \"properties\": [ { \"externalId\": ");

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => Adapter(handler).PullAsync(Context, default));
        Assert.Equal("The sandbox source returned a malformed snapshot payload.", exception.Message);
    }

    [Fact]
    public async Task Pull_names_the_field_a_record_is_missing()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK,
            """{"spaces":[{"externalId":"SB-SPACE-9","code":"301"}]}""");

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => Adapter(handler).PullAsync(Context, default));
        Assert.Equal("The sandbox snapshot is missing required field 'propertyExternalId' at spaces[0].", exception.Message);
    }

    [Fact]
    public async Task Pull_reports_an_unreachable_source_without_transport_detail()
    {
        var handler = StubHandler.Throwing(new HttpRequestException("No such host is known. (sandbox.example.test:443)"));

        var exception = await Assert.ThrowsAsync<IntegrationPullException>(() => Adapter(handler).PullAsync(Context, default));
        Assert.Equal("The sandbox source could not be reached.", exception.Message);
    }

    [Fact]
    public void Sandbox_adapter_declares_a_full_property_management_snapshot()
    {
        var adapter = Adapter(StubHandler.Returning(HttpStatusCode.OK, "{}"));

        Assert.Equal("sandbox", adapter.SourceSystem);
        Assert.Equal(IntegrationCategory.PropertyManagement, adapter.Descriptor.Category);
        Assert.False(adapter.Descriptor.SupportsIncrementalPull);
        Assert.Equal(IntegrationAdapterDescriptor.AllKinds, adapter.Descriptor.SuppliedKinds);
    }

    [Fact]
    public void Both_shipped_adapters_coexist_in_the_catalog()
    {
        // The registration the API makes: MockIntegrationAdapter then SandboxIntegrationAdapter.
        var catalog = new IntegrationCatalog(
            [new MockIntegrationAdapter(), Adapter(StubHandler.Returning(HttpStatusCode.OK, "{}"))]);

        Assert.Equal(2, catalog.Available.Count);
        Assert.IsType<MockIntegrationAdapter>(catalog.Resolve("mock"));
        Assert.IsType<SandboxIntegrationAdapter>(catalog.Resolve("sandbox"));
        Assert.Null(catalog.Resolve("yardi"));
    }

    [Fact]
    public void The_apis_integration_registrations_compose_into_a_working_catalog()
    {
        // Mirrors src/PropFlow.Api/Program.cs:90-93. Registering a second IIntegrationAdapter is
        // the first time IntegrationCatalog's duplicate-detection path is live in a real DI
        // graph, so prove the graph still resolves rather than assuming it.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(BaseAddress, "s"));
        services.AddHttpClient();
        services.AddSingleton<IIntegrationAdapter, MockIntegrationAdapter>();
        services.AddSingleton<IIntegrationAdapter, SandboxIntegrationAdapter>();
        services.AddSingleton<IIntegrationSecretStore, ConfiguredIntegrationSecretStore>();
        services.AddSingleton<IIntegrationCatalog, IntegrationCatalog>();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var catalog = provider.GetRequiredService<IIntegrationCatalog>();

        Assert.Equal(["mock", "sandbox"], catalog.Available.Select(a => a.SourceSystem));
        Assert.NotNull(catalog.Resolve("mock"));
        Assert.NotNull(catalog.Resolve("sandbox"));
    }

    [Fact]
    public async Task Secret_store_resolves_by_connection_id_and_reports_none_otherwise()
    {
        var store = new ConfiguredIntegrationSecretStore(Configuration(BaseAddress, "sandbox-secret-for-this-connection"));

        var found = await store.ResolveAsync(ConnectionId, default);
        Assert.Equal(IntegrationSecretOutcome.Found, found.Outcome);
        Assert.Equal("sandbox-secret-for-this-connection", found.Secret);

        var other = await store.ResolveAsync(Guid.NewGuid(), default);
        Assert.Equal(IntegrationSecretOutcome.NotConfigured, other.Outcome);
        Assert.Null(other.Secret);
    }

    [Fact]
    public async Task Secret_store_treats_a_blank_value_as_not_configured()
    {
        var store = new ConfiguredIntegrationSecretStore(Configuration(BaseAddress, "   "));

        var lookup = await store.ResolveAsync(ConnectionId, default);
        Assert.Equal(IntegrationSecretOutcome.NotConfigured, lookup.Outcome);
        Assert.Null(lookup.Secret);
    }

    [Fact]
    public async Task Secret_store_key_is_case_insensitive_in_the_guid()
    {
        var store = new ConfiguredIntegrationSecretStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Integrations:Secrets:{ConnectionId.ToString("D").ToUpperInvariant()}"] = "upper-cased-key"
            })
            .Build());

        var lookup = await store.ResolveAsync(ConnectionId, default);
        Assert.Equal(IntegrationSecretOutcome.Found, lookup.Outcome);
        Assert.Equal("upper-cased-key", lookup.Secret);
    }

    [Fact]
    public void Sync_sanitizes_an_adapter_message_that_FailSync_would_reject()
    {
        // FailSync rejects control characters and anything over ErrorMaxLength; an adapter that
        // ignores the single-line contract must not turn a failed sync into a 500.
        var multiline = EfIntegrationOperations.Sanitize("Request failed.\r\n   at Some.Frame()\n\tat Other.Frame()");
        Assert.Equal("Request failed. at Some.Frame() at Other.Frame()", multiline);
        Assert.DoesNotContain(multiline, c => char.IsControl(c));

        var long_ = EfIntegrationOperations.Sanitize(new string('x', IntegrationConnection.ErrorMaxLength + 500));
        Assert.Equal(IntegrationConnection.ErrorMaxLength, long_.Length);

        Assert.Equal("The adapter failed without a message.", EfIntegrationOperations.Sanitize(" \r\n\t "));

        // A message that already complies is returned unchanged.
        const string clean = "The sandbox source returned 503.";
        Assert.Equal(clean, EfIntegrationOperations.Sanitize(clean));
    }

    private static SandboxIntegrationAdapter Adapter(StubHandler handler)
    {
        var configuration = Configuration(BaseAddress, "sandbox-secret-for-this-connection");
        return new SandboxIntegrationAdapter(
            new StubClientFactory(handler), configuration, new ConfiguredIntegrationSecretStore(configuration));
    }

    private static IConfiguration Configuration(string? baseAddress, string? secret)
    {
        var values = new Dictionary<string, string?>();
        if (baseAddress is not null) values[SandboxIntegrationAdapter.BaseAddressKey] = baseAddress;
        if (secret is not null) values[ConfiguredIntegrationSecretStore.KeyFor(ConnectionId)] = secret;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // Testing an IHttpClientFactory consumer without a socket: hand the HttpClient a handler that
    // answers from memory and records what it was asked for.
    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status;
        private string body = "";
        private Exception? throws;

        public HttpRequestMessage? LastRequest { get; private set; }

        public static StubHandler Returning(HttpStatusCode status, string body) =>
            new() { status = status, body = body };

        public static StubHandler Throwing(Exception exception) => new() { throws = exception };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (throws is not null) return Task.FromException<HttpResponseMessage>(throws);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
