using PropFlow.Application.Attachments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PropFlow.Infrastructure.Attachments;

public sealed class LocalAttachmentStorage(IConfiguration configuration, IHostEnvironment environment) : IAttachmentStorage
{
    private const int MaxKeyLength = 300;
    private readonly string root = ResolveRoot(configuration, environment);

    public async Task StoreAsync(string storageKey, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await content.CopyToAsync(output, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true)
            : null);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > MaxKeyLength ||
            Path.IsPathRooted(storageKey) || storageKey.Contains("..", StringComparison.Ordinal) ||
            storageKey.Contains('\0'))
            throw new ArgumentException("Invalid attachment storage key.", nameof(storageKey));
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid attachment storage key.", nameof(storageKey));
        return path;
    }

    private static string ResolveRoot(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Attachments:RootPath"];
        if (string.IsNullOrWhiteSpace(configured) && (environment.IsProduction() || environment.IsStaging()))
            throw new InvalidOperationException("Production and staging require Attachments:RootPath to be configured.");
        return configured ?? Path.Combine(Path.GetTempPath(), "propflow-attachments");
    }
}
