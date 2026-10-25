using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DollarShark.Application.Statements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace DollarShark.Infrastructure.Statements;

public sealed class LocalPrivateStatementStorage : IStatementStorage
{
    private static readonly Regex SafeStorageKey = new("^[a-f0-9]{32}\\.(pdf|csv|xlsx)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string rootPath;

    public LocalPrivateStatementStorage(IWebHostEnvironment environment, IOptions<StatementStorageOptions> options)
    {
        var configured = options.Value.RootPath;
        rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DollarShark", "Statements")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));

        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var webRootPrefix = Path.TrimEndingDirectorySeparator(webRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (rootPath.Equals(webRoot, comparison) || rootPath.StartsWith(webRootPrefix, comparison))
            throw new InvalidOperationException("Statement storage must be outside the public web root.");
    }

    public async Task<StatementStorageResult> StoreAsync(Stream content, string extension, long maxFileSizeBytes, CancellationToken cancellationToken)
    {
        var contentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => throw new InvalidOperationException("Unsupported statement extension.")
        };
        Directory.CreateDirectory(rootPath);
        var temporaryPath = Path.Combine(rootPath, $"{Guid.NewGuid():N}.uploading");
        var storageKey = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var finalPath = GetPath(storageKey);
        long totalBytes = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];

        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > maxFileSizeBytes) throw new StatementFileTooLargeException(maxFileSizeBytes);
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return new StatementStorageResult(storageKey, totalBytes, contentType,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(storageKey);
        TryDelete(path);
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream content = new FileStream(GetPath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(content);
    }

    private string GetPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !SafeStorageKey.IsMatch(storageKey))
            throw new ArgumentException("Invalid statement storage key.", nameof(storageKey));
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, storageKey));
        var prefix = Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(prefix, comparison)) throw new ArgumentException("Invalid statement storage key.", nameof(storageKey));
        return fullPath;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
