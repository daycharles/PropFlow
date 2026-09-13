using System.Security.Cryptography;
using System.Text;
using PropFlow.Domain;

namespace PropFlow.Domain.Communications;

public sealed class DocumentTemplate(Guid organizationId, Guid id, string name, string content)
    : TenantEntity(organizationId, id)
{
    public string Name { get; private set; } = Required(name, nameof(name), 200);
    public string Content { get; private set; } = Required(content, nameof(content), 100000);
    public int Version { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public void Revise(string content) { Content = Required(content, nameof(content), 100000); Version++; }
    public void Archive() => IsActive = false;
    public void Activate() => IsActive = true;
    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}

public enum DocumentPacketStatus { Draft, Sent, PartiallySigned, Signed, Expired, Failed }

public sealed class DocumentPacket(Guid organizationId, Guid id, Guid templateId, string title, string renderedContent,
    DateTimeOffset? expiresAt, int templateVersion) : TenantEntity(organizationId, id)
{
    public Guid TemplateId { get; private set; } = templateId;
    public string Title { get; private set; } = title.Trim();
    public string RenderedContent { get; private set; } = renderedContent.Trim();
    public int TemplateVersion { get; private set; } = templateVersion;
    public string IntegrityHash { get; private set; } = Hash(renderedContent);
    public DocumentPacketStatus Status { get; private set; } = DocumentPacketStatus.Draft;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; private set; } = expiresAt?.ToUniversalTime();
    public DateTimeOffset? RetainUntil { get; private set; }
    public void SetRetention(DateTimeOffset? retainUntil) => RetainUntil = retainUntil?.ToUniversalTime();
    public void Send() => Status = DocumentPacketStatus.Sent;
    public void MarkSigned() => Status = DocumentPacketStatus.Signed;
    public void MarkFailed() => Status = DocumentPacketStatus.Failed;
    public void Expire(DateTimeOffset now) { if (ExpiresAt is { } expiry && expiry <= now) Status = DocumentPacketStatus.Expired; }
    public bool HasIntegrity => IntegrityHash == Hash(RenderedContent);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum SignatureRequestStatus { Pending, Signed, Declined, Failed }

public sealed class SignatureRequest(Guid organizationId, Guid id, Guid packetId, string signerId, string signerName)
    : TenantEntity(organizationId, id)
{
    public Guid PacketId { get; private set; } = packetId;
    public string SignerId { get; private set; } = Required(signerId, nameof(signerId), 200);
    public string SignerName { get; private set; } = Required(signerName, nameof(signerName), 200);
    public SignatureRequestStatus Status { get; private set; } = SignatureRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SignedAt { get; private set; }
    public string? EvidenceHash { get; private set; }
    public int AttemptCount { get; private set; }
    public string? FailureReason { get; private set; }
    public void MarkSigned(DateTimeOffset at, string evidenceHash) { SignedAt = at.ToUniversalTime(); EvidenceHash = Required(evidenceHash, nameof(evidenceHash), 128); Status = SignatureRequestStatus.Signed; }
    public void RecordFailure(string reason, int maxAttempts) { AttemptCount++; FailureReason = Required(reason, nameof(reason), 1000); Status = AttemptCount >= maxAttempts ? SignatureRequestStatus.Failed : SignatureRequestStatus.Pending; }
    private static string Required(string value, string name, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
