namespace PropFlow.Domain.Work;

public sealed class Attachment : TenantEntity
{
    private Attachment(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static Attachment Create(Guid organizationId, Guid id, Guid workId, string fileName,
        string contentType, long length, string storageKey, bool residentVisible, DateTimeOffset createdAt,
        DateTimeOffset? retainUntil)
    {
        if (workId == Guid.Empty) throw new ArgumentException("Work item is required.", nameof(workId));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Trim().Length > 255)
            throw new ArgumentException("File name must contain 1 to 255 characters.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Trim().Length > 150)
            throw new ArgumentException("Content type is required.", nameof(contentType));
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("Storage key is required.", nameof(storageKey));
        if (retainUntil is { } expiry && expiry < createdAt) throw new ArgumentException("Retention must be after creation.", nameof(retainUntil));

        return new Attachment(organizationId, id)
        {
            WorkId = workId,
            FileName = Path.GetFileName(fileName.Trim()),
            ContentType = contentType.Trim().ToLowerInvariant(),
            Length = length,
            StorageKey = storageKey.Trim(),
            ResidentVisible = residentVisible,
            CreatedAt = createdAt.ToUniversalTime(),
            RetainUntil = retainUntil?.ToUniversalTime()
        };
    }

    public Guid WorkId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Length { get; private set; }
    public string StorageKey { get; private set; } = "";
    public bool ResidentVisible { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RetainUntil { get; private set; }
}
