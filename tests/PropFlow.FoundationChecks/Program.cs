using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Communications;
using PropFlow.Application.Events;
using PropFlow.Domain;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Work;

var organization = Guid.NewGuid();
var actor = Guid.NewGuid();
var checks = new List<(string Name, Action Run)>
{
    ("Reject anonymous organization claim", () => Reject<TenantAccessException>(() => TenantAccess.Resolve(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantAccess.OrganizationClaim, organization.ToString())]))))),
    ("Reject missing organization", () => Reject<TenantAccessException>(() => TenantAccess.Resolve(Principal()))),
    ("Reject empty organization", () => Reject<TenantAccessException>(() => TenantAccess.Resolve(Principal(Guid.Empty.ToString())))),
    ("Reject malformed organization", () => Reject<TenantAccessException>(() => TenantAccess.Resolve(Principal("invalid")))) ,
    ("Reject ambiguous organization", () => Reject<TenantAccessException>(() => TenantAccess.Resolve(Principal(organization.ToString(), Guid.NewGuid().ToString())))),
    ("Resolve authenticated organization", () => Assert(TenantAccess.Resolve(Principal(organization.ToString())) == organization)),
    ("Reject unscoped entity", () => Reject<ArgumentException>(() => new WorkItem(Guid.Empty, Guid.NewGuid(), "Repair HVAC"))),
    ("Reject blank title", () => Reject<ArgumentException>(() => new WorkItem(organization, Guid.NewGuid(), " "))),
    ("Assignment preserves old/new values and actor", () =>
    {
        var work = new WorkItem(organization, Guid.NewGuid(), "Pest control");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var when = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");
        var created = work.AssignVendor(first, actor, when)!;
        var changed = work.AssignVendor(second, actor, when)!;
        Assert(created.PreviousVendorId is null && changed.PreviousVendorId == first);
        Assert(changed.VendorId == second && work.VendorId == second);
        Assert(changed.OrganizationId == organization && changed.ActorId == actor && changed.WorkId == work.Id);
        Assert(changed.OccurredAt.Offset == TimeSpan.Zero && changed.OccurredAt == when);
        Assert(created.EventId != changed.EventId);
        Assert(work.AssignVendor(second, actor, when) is null);
    }),
    ("Invalid assignment leaves work unchanged", () =>
    {
        var work = new WorkItem(organization, Guid.NewGuid(), "Repair");
        Reject<ArgumentException>(() => work.AssignVendor(Guid.Empty, actor, DateTimeOffset.UtcNow));
        Reject<ArgumentException>(() => work.AssignVendor(Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
        Assert(work.VendorId is null);
    }),
    ("Handler failure stops subsequent dispatch", () =>
    {
        var later = new RecordingHandler();
        var dispatcher = new InProcessEventDispatcher([new FailingHandler(), later]);
        var change = new WorkItem(organization, Guid.NewGuid(), "Repair").AssignVendor(Guid.NewGuid(), actor, DateTimeOffset.UtcNow)!;
        Reject<InvalidOperationException>(() => dispatcher.DispatchAsync(change).GetAwaiter().GetResult());
        Assert(!later.Called);
    }),
    ("Cancellation prevents dispatch", () =>
    {
        var handler = new RecordingHandler();
        var dispatcher = new InProcessEventDispatcher([handler]);
        var change = new WorkItem(organization, Guid.NewGuid(), "Repair").AssignVendor(Guid.NewGuid(), actor, DateTimeOffset.UtcNow)!;
        Reject<OperationCanceledException>(() => dispatcher.DispatchAsync(change, new CancellationToken(true)).GetAwaiter().GetResult());
        Assert(!handler.Called);
    }),
    ("Template rejects a blank name", () => Reject<ArgumentException>(() =>
        new MessageTemplate(organization, Guid.NewGuid(), " ", MessageChannel.Sms, null, "Pest control is on the way."))),
    ("Email template requires a subject", () => Reject<ArgumentException>(() =>
        new MessageTemplate(organization, Guid.NewGuid(), "Completion", MessageChannel.Email, " ", "The work is complete."))),
    ("SMS template rejects a subject", () => Reject<ArgumentException>(() =>
        new MessageTemplate(organization, Guid.NewGuid(), "On the way", MessageChannel.Sms, "Subject", "On the way."))),
    ("Template revision revalidates content and toggles active state", () =>
    {
        var template = new MessageTemplate(organization, Guid.NewGuid(), "Scheduled", MessageChannel.Email, "Visit", "Body");
        Reject<ArgumentException>(() => template.Revise(null, "Updated"));
        template.Revise("New subject", "Updated body");
        Assert(template.Subject == "New subject" && template.Body == "Updated body");
        template.Deactivate();
        Assert(!template.IsActive);
        template.Activate();
        Assert(template.IsActive);
    }),
    ("Renderer substitutes supplied values", () => Assert(
        new TemplateRenderer().Render("Pest control is scheduled for {{ day }} at {{time}}.",
            new Dictionary<string, string> { ["day"] = "Friday", ["time"] = "9:00 AM" })
        == "Pest control is scheduled for Friday at 9:00 AM.")),
    ("Renderer rejects a placeholder with no supplied value", () => Reject<TemplateRenderException>(() =>
        new TemplateRenderer().Render("Hello {{ name }}", new Dictionary<string, string>()))),
    ("Renderer rejects a malformed placeholder", () => Reject<TemplateRenderException>(() =>
        new TemplateRenderer().Render("Hello {{ 1nvalid }}", new Dictionary<string, string> { ["x"] = "y" }))),
    ("Renderer lists distinct placeholders", () =>
    {
        var names = new TemplateRenderer().Placeholders("{{a}} {{ b }} {{a}}");
        Assert(names.Count == 2 && names.Contains("a") && names.Contains("b"));
    }),
    ("Consent blocks contact until explicitly granted", () =>
    {
        var when = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");
        var consent = ChannelConsent.Unset(MessageChannel.Sms);
        Assert(!consent.AllowsContact);
        var granted = consent.Grant(when);
        Assert(granted.AllowsContact && granted.DecidedAt == when && granted.DecidedAt!.Value.Offset == TimeSpan.Zero);
        Assert(!granted.Revoke(when).AllowsContact);
    }),
    ("Outbound message enforces channel shape and trims", () =>
    {
        Reject<ArgumentException>(() => new OutboundMessage(MessageChannel.Sms, " ", null, "Body"));
        Reject<ArgumentException>(() => new OutboundMessage(MessageChannel.Email, "resident@example.test", null, "Body"));
        Reject<ArgumentException>(() => new OutboundMessage(MessageChannel.Sms, "+15550001111", "Subject", "Body"));
        var message = new OutboundMessage(MessageChannel.Email, " resident@example.test ", " Visit ", " Body ");
        Assert(message.RecipientAddress == "resident@example.test" && message.Subject == "Visit" && message.Body == "Body");
    })
};

foreach (var check in checks)
{
    check.Run();
    Console.WriteLine($"PASS {check.Name}");
}
Console.WriteLine($"{checks.Count} foundation checks passed.");

static ClaimsPrincipal Principal(params string[] organizations) => new(new ClaimsIdentity(
    organizations.Select(id => new Claim(TenantAccess.OrganizationClaim, id)), "test"));
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
sealed class RecordingHandler : IDomainEventHandler
{
    public bool Called { get; private set; }
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) { Called = true; return Task.CompletedTask; }
}
sealed class FailingHandler : IDomainEventHandler
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("Simulated handler failure");
}
