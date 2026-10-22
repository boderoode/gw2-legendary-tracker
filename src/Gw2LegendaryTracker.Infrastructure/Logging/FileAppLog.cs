using System.Globalization;
using System.Text;
using Gw2LegendaryTracker.Core.Logging;

namespace Gw2LegendaryTracker.Infrastructure.Logging;

/// <summary>
/// Writes <c>app-yyyyMMdd.log</c> files (UTC date), one per day, keeping a week and at most 1 MB per day.
/// Everything is redacted first (<see cref="LogRedactor"/>), and a failure to write is swallowed:
/// logging must never be the reason the app stops.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    public const int DefaultRetentionDays = 7;

    public const long DefaultMaxFileBytes = 1_000_000;

    private const int MaxMessageLength = 4000;
    private const string FilePrefix = "app-";
    private const string FileSuffix = ".log";
    private const string DateFormat = "yyyyMMdd";

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly long _maxFileBytes;
    private readonly object _gate = new();
    private DateOnly? _fullDay;

    public FileAppLog(string directory, TimeProvider? time = null, int retentionDays = DefaultRetentionDays, long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _time = time ?? TimeProvider.System;
        _maxFileBytes = maxFileBytes;
        DeleteOldFiles(retentionDays);
    }

    /// <summary>The folder that holds the log files, so the user can open it and share a file.</summary>
    public string Directory => _directory;

    public void Info(string message) => Write("INF", message, null);

    public void Warning(string message) => Write("WRN", message, null);

    public void Failure(string message, Exception? exception = null) => Write("ERR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = _time.GetUtcNow();
            var text = new StringBuilder()
                .Append(now.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(Flatten(LogRedactor.Redact(message)));

            if (exception is not null)
            {
                // ToString() carries the type, message, stack trace and inner exceptions.
                text.AppendLine().Append(Indent(LogRedactor.Redact(exception.ToString())));
            }

            text.AppendLine();

            lock (_gate)
            {
                Append(DateOnly.FromDateTime(now.UtcDateTime), text.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Not being able to log (read-only disk, a locked file) must not affect the app.
        }
    }

    private void Append(DateOnly day, string text)
    {
        if (_fullDay == day)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{FilePrefix}{day.ToString(DateFormat, CultureInfo.InvariantCulture)}{FileSuffix}");
        if (File.Exists(path) && new FileInfo(path).Length >= _maxFileBytes)
        {
            // One closing line, then silence for the rest of the day, so a runaway loop cannot fill the disk.
            File.AppendAllText(path, "-- Logbestand vol: verdere berichten van vandaag worden niet bewaard.\n", Encoding.UTF8);
            _fullDay = day;
            return;
        }

        File.AppendAllText(path, text, Encoding.UTF8);
    }

    private void DeleteOldFiles(int retentionDays)
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return;
            }

            var oldest = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddDays(-retentionDays);
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileSuffix}"))
            {
                var name = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];
                if (DateOnly.TryParseExact(name, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < oldest)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Old files that cannot be removed now are tried again at the next start.
        }
    }

    private static string Flatten(string message)
    {
        var flat = message.ReplaceLineEndings(" ");
        return flat.Length > MaxMessageLength ? flat[..MaxMessageLength] + "…" : flat;
    }

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.ReplaceLineEndings("\n").Split('\n').Select(line => "    " + line));
}
