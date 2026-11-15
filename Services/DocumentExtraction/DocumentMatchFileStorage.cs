using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentMatchFileStorage(
    IHostEnvironment environment,
    IOptions<DocumentMatchingOptions> options)
{
    private readonly string root = Path.GetFullPath(
        Path.IsPathRooted(options.Value.JobStoragePath)
            ? options.Value.JobStoragePath
            : Path.Combine(environment.ContentRootPath, options.Value.JobStoragePath));

    public async Task<IReadOnlyList<StoredDocumentMatchFile>> SaveAsync(
        Guid jobId,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var directory = JobDirectory(jobId);
        Directory.CreateDirectory(directory);
        var stored = new List<StoredDocumentMatchFile>(files.Count);
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));
                var extension = Path.GetExtension(originalName).ToLowerInvariant();
                if (extension is not (".xlsx" or ".pdf" or ".jpg" or ".jpeg" or ".png"))
                    extension = ".upload";
                var storageName = $"{index:D4}{extension}";
                var path = Path.Combine(directory, storageName);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await file.CopyToAsync(output, cancellationToken);
                stored.Add(new StoredDocumentMatchFile
                {
                    FileIndex = index,
                    StorageName = storageName,
                    OriginalName = originalName,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    Length = file.Length
                });
            }
            return stored;
        }
        catch
        {
            Delete(jobId);
            throw;
        }
    }

    public FileStream OpenRead(Guid jobId, StoredDocumentMatchFile file) =>
        new(Path.Combine(JobDirectory(jobId), file.StorageName), FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void Delete(Guid jobId)
    {
        var directory = JobDirectory(jobId);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private string JobDirectory(Guid jobId)
    {
        var path = Path.GetFullPath(Path.Combine(root, jobId.ToString("N")));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid document job storage path.");
        return path;
    }
}
