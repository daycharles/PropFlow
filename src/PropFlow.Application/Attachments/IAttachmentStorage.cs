namespace PropFlow.Application.Attachments;

public interface IAttachmentStorage
{
    Task StoreAsync(string storageKey, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
