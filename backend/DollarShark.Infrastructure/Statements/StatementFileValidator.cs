using System.IO.Compression;
using System.Text;
using DollarShark.Application.Statements;

namespace DollarShark.Infrastructure.Statements;

public sealed class StatementFileValidator : IStatementFileValidator
{
    public async Task<bool> IsValidAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        if (!content.CanSeek || content.Length == 0) return false;
        var originalPosition = content.Position;
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => await IsPdfAsync(content, cancellationToken),
                ".csv" => await IsCsvAsync(content, cancellationToken),
                ".xlsx" => IsXlsx(content),
                _ => false
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException or IOException or ArgumentException)
        {
            return false;
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static async Task<bool> IsPdfAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.Length < 10) return false;
        var header = new byte[5];
        content.Position = 0;
        if (await content.ReadAsync(header.AsMemory(), cancellationToken) != header.Length || Encoding.ASCII.GetString(header) != "%PDF-")
            return false;

        var tailSize = (int)Math.Min(content.Length, 1024);
        var tail = new byte[tailSize];
        content.Position = content.Length - tailSize;
        var totalRead = 0;
        while (totalRead < tailSize)
        {
            var read = await content.ReadAsync(tail.AsMemory(totalRead, tailSize - totalRead), cancellationToken);
            if (read == 0) break;
            totalRead += read;
        }
        return Encoding.ASCII.GetString(tail, 0, totalRead).Contains("%%EOF", StringComparison.Ordinal);
    }

    private static async Task<bool> IsCsvAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        string? firstNonEmptyLine = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Contains('\0')) return false;
            if (firstNonEmptyLine is null && !string.IsNullOrWhiteSpace(line)) firstNonEmptyLine = line;
        }
        return firstNonEmptyLine is not null &&
            (firstNonEmptyLine.Contains(',') || firstNonEmptyLine.Contains(';') || firstNonEmptyLine.Contains('\t'));
    }

    private static bool IsXlsx(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        return archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry("xl/workbook.xml") is not null;
    }
}
