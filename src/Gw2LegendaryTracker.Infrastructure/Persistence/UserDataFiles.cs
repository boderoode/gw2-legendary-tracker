using System.Globalization;
using System.Text;
using Gw2LegendaryTracker.Core.Export;

namespace Gw2LegendaryTracker.Infrastructure.Persistence;

public sealed record FileReadResult(string? Text, string? Error);

/// <summary>File handling for export and import: complete files only, bounded reads, automatic back-ups.</summary>
public static class UserDataFiles
{
    public const int DefaultBackupsToKeep = 5;

    private const string BackupPrefix = "before-import-";

    /// <summary>Writes to a temporary file first, so a crash or full disk never leaves a half-written export.</summary>
    public static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Reads an import file. Refuses files that are missing, unreadable or too large to be an export.</summary>
    public static async Task<FileReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new FileReadResult(null, "Het bestand bestaat niet (meer).");
            }

            if (info.Length > UserDataSerializer.MaxBytes)
            {
                return new FileReadResult(null, "Het bestand is te groot om een export van deze app te zijn.");
            }

            return new FileReadResult(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileReadResult(null, $"Het bestand kon niet worden gelezen: {ex.Message}");
        }
    }

    /// <summary>Saves a copy of the current data before an import and removes all but the newest <paramref name="keep"/> copies.</summary>
    public static async Task<string> BackupAsync(string folder, string content, DateTimeOffset now, int keep = DefaultBackupsToKeep, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var path = Path.Combine(folder, $"{BackupPrefix}{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
        await WriteAtomicAsync(path, content, cancellationToken).ConfigureAwait(false);

        // The names sort by time, so the oldest come first.
        var surplus = Directory.EnumerateFiles(folder, $"{BackupPrefix}*.json")
            .Order(StringComparer.Ordinal)
            .SkipLast(Math.Max(keep, 1));
        foreach (var old in surplus)
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An old back-up that cannot be removed now is tried again at the next import.
            }
        }

        return path;
    }
}
