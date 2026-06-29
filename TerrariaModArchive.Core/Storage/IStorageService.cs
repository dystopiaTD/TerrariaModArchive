namespace TerrariaModArchive.Core.Storage;

public interface IStorageService
{
    Task<string> SaveFileAsync(string fileName, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> GetFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
}
