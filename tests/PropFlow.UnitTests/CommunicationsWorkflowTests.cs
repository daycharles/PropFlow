using System.Text;
using System.Security.Cryptography;
using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class CommunicationsWorkflowTests
{
    private static readonly Guid Org = Guid.NewGuid();

    [Fact]
    public void Conversation_messages_are_threaded_and_tamper_evident()
    {
        var conversation = new Conversation(Org, Guid.NewGuid(), "Repair update", "+15550001111");
        var message = new ConversationMessage(Org, Guid.NewGuid(), conversation.Id,
            ConversationMessageDirection.Inbound, MessageChannel.Sms, "Please call me", "resident-1", DateTimeOffset.UtcNow);

        Assert.Equal(conversation.Id, message.ConversationId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Please call me"))), message.IntegrityHash);
    }

    [Fact]
    public void Campaign_and_unsubscribe_values_are_validated_and_normalized()
    {
        var campaign = new Campaign(Org, Guid.NewGuid(), "Spring", MessageChannel.Email, "Hello", "Welcome", null);
        campaign.Publish();
        var unsubscribe = new ChannelUnsubscribe(Org, Guid.NewGuid(), MessageChannel.Email, " resident@example.test ", DateTimeOffset.UtcNow);

        Assert.True(campaign.IsPublished);
        Assert.Equal("resident@example.test", unsubscribe.Address);
        Assert.Throws<ArgumentException>(() => new Campaign(Org, Guid.NewGuid(), "", MessageChannel.Email, "x", "y", null));
    }

    [Fact]
    public void Document_packet_captures_template_version_and_signature_evidence()
    {
        var template = new DocumentTemplate(Org, Guid.NewGuid(), "Lease", "Tenant: {{name}}");
        template.Revise("Tenant: {{name}}\nRent: {{rent}}");
        var packet = new DocumentPacket(Org, Guid.NewGuid(), template.Id, "Lease packet", template.Content, DateTimeOffset.UtcNow.AddDays(1), template.Version);
        var signer = new SignatureRequest(Org, Guid.NewGuid(), packet.Id, "user-1", "Alex Resident");

        Assert.Equal(2, packet.TemplateVersion);
        Assert.True(packet.HasIntegrity);
        signer.MarkSigned(DateTimeOffset.UtcNow, packet.IntegrityHash);
        packet.MarkSigned();
        Assert.Equal(SignatureRequestStatus.Signed, signer.Status);
        Assert.Equal(DocumentPacketStatus.Signed, packet.Status);
        Assert.Equal(packet.IntegrityHash, signer.EvidenceHash);
    }

    [Fact]
    public void Signature_provider_failures_are_retryable_until_the_cap()
    {
        var signer = new SignatureRequest(Org, Guid.NewGuid(), Guid.NewGuid(), "user-1", "Alex");
        signer.RecordFailure("provider timeout", 2);
        Assert.Equal(SignatureRequestStatus.Pending, signer.Status);
        signer.RecordFailure("provider timeout", 2);
        Assert.Equal(SignatureRequestStatus.Failed, signer.Status);
    }
}
