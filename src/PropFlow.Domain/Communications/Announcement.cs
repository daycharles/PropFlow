namespace PropFlow.Domain.Communications;

public enum AnnouncementStatus { Draft, Published, Archived }

public sealed class Announcement(Guid organizationId, Guid id, string title, string body, DateTimeOffset? expiresAt)
    : TenantEntity(organizationId, id)
{
    public string Title { get; private set; } = Required(title, nameof(title), 200);
    public string Body { get; private set; } = Required(body, nameof(body), 4000);
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; private set; } = expiresAt?.ToUniversalTime();
    public AnnouncementStatus Status { get; private set; } = AnnouncementStatus.Draft;

    public void Update(string title, string body, DateTimeOffset? expiresAt)
    {
        Title = Required(title, nameof(title), 200);
        Body = Required(body, nameof(body), 4000);
        ExpiresAt = expiresAt?.ToUniversalTime();
    }

    public void Publish() => Status = AnnouncementStatus.Published;
    public void Archive() => Status = AnnouncementStatus.Archived;

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
