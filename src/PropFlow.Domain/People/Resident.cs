using PropFlow.Domain.Communications;

namespace PropFlow.Domain.People;

// A person who lives at one of the organization's spaces and may receive resident
// communications. Contact fields are validated as single-line text — a resident name flows
// into a rendered message as a template value, so a CR/LF here is an injection vector.
public sealed class Resident : TenantEntity
{
    // EF materialization only.
    private Resident(Guid organizationId, Guid id) : base(organizationId, id) { }

    public Resident(Guid organizationId, Guid id, string fullName, string? email, string? phone)
        : base(organizationId, id)
    {
        FullName = MessageText.RequireSingleLine(fullName, nameof(fullName), 200);
        Email = NormalizeOptional(email, nameof(email), 254);
        Phone = NormalizeOptional(phone, nameof(phone), 40);
    }

    public string FullName { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public ConsentDecision SmsConsent { get; private set; }
    public DateTimeOffset? SmsConsentAt { get; private set; }
    public ConsentDecision EmailConsent { get; private set; }
    public DateTimeOffset? EmailConsentAt { get; private set; }

    public void Rename(string fullName) => FullName = MessageText.RequireSingleLine(fullName, nameof(fullName), 200);

    public void UpdateContact(string? email, string? phone)
    {
        Email = NormalizeOptional(email, nameof(email), 254);
        Phone = NormalizeOptional(phone, nameof(phone), 40);
    }

    public void SetConsent(MessageChannel channel, bool granted, DateTimeOffset at)
    {
        var decision = granted ? ConsentDecision.Granted : ConsentDecision.Revoked;
        var when = at.ToUniversalTime();
        switch (channel)
        {
            case MessageChannel.Sms:
                SmsConsent = decision;
                SmsConsentAt = when;
                break;
            case MessageChannel.Email:
                EmailConsent = decision;
                EmailConsentAt = when;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown message channel.");
        }
    }

    // A resident may be contacted on a channel only with an explicit grant and an address.
    public bool AllowsContact(MessageChannel channel) => channel switch
    {
        MessageChannel.Sms => SmsConsent == ConsentDecision.Granted && !string.IsNullOrWhiteSpace(Phone),
        MessageChannel.Email => EmailConsent == ConsentDecision.Granted && !string.IsNullOrWhiteSpace(Email),
        _ => false
    };

    private static string? NormalizeOptional(string? value, string parameter, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : MessageText.RequireSingleLine(value, parameter, maxLength);
}
