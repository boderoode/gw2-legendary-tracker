using Gw2LegendaryTracker.Core.Logging;
using Gw2LegendaryTracker.Infrastructure.Logging;

namespace Gw2LegendaryTracker.Tests;

public class LogRedactorTests
{
    private const string Gw2Key = TestData.Key;
    private const string TreasuresKey = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

    [Fact]
    public void A_guild_wars_key_is_removed_completely()
    {
        var result = LogRedactor.Redact($"Controle van key {Gw2Key} mislukt");

        Assert.Equal($"Controle van key {LogRedactor.SecretMask} mislukt", result);
        Assert.DoesNotContain("11111111", result, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gw2treasures_key_is_removed()
    {
        Assert.Equal($"key {LogRedactor.SecretMask}", LogRedactor.Redact($"key {TreasuresKey}"));
    }

    [Fact]
    public void Bearer_tokens_are_removed_whatever_their_shape()
    {
        var result = LogRedactor.Redact("Authorization: Bearer abc.DEF-123_xyz verzonden");

        Assert.DoesNotContain("abc.DEF", result, StringComparison.Ordinal);
        Assert.Contains("Bearer " + LogRedactor.SecretMask, result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://api.guildwars2.com/v2/account?access_token=SECRET123&x=1", "access_token=" + LogRedactor.SecretMask + "&x=1")]
    [InlineData("GET /items?apiKey=SECRET123", "apiKey=" + LogRedactor.SecretMask)]
    [InlineData("opts api_key=SECRET123 end", "api_key=" + LogRedactor.SecretMask + " end")]
    [InlineData("?key=SECRET123", "key=" + LogRedactor.SecretMask)]
    public void Key_like_url_parameters_lose_their_value(string input, string expectedFragment)
    {
        var result = LogRedactor.Redact(input);

        Assert.Contains(expectedFragment, result, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET123", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Users\Bo\AppData\Local\Gw2LegendaryTracker\tracker.db", @"%USERPROFILE%\AppData\Local\Gw2LegendaryTracker\tracker.db")]
    [InlineData("c:/users/Jan Jansen/x", "%USERPROFILE%/x")]
    public void The_windows_user_name_in_paths_is_replaced(string input, string expected)
    {
        var result = LogRedactor.Redact(input);

        Assert.DoesNotContain("Bo\\", result, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%", result, StringComparison.Ordinal);
        if (!input.Contains(' ', StringComparison.Ordinal))
        {
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Several_secrets_in_one_message_are_all_removed()
    {
        var result = LogRedactor.Redact($"{Gw2Key} en {TreasuresKey} en Bearer {Gw2Key}");

        Assert.DoesNotContain(Gw2Key, result, StringComparison.Ordinal);
        Assert.DoesNotContain(TreasuresKey, result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Synchronisatie gelukt: 4 onderdelen")]
    [InlineData("Item 30704 heeft 13 recepten")]
    [InlineData("")]
    public void Ordinary_text_is_left_alone(string text)
    {
        Assert.Equal(text, LogRedactor.Redact(text));
    }

    [Fact]
    public void Null_gives_an_empty_string()
    {
        Assert.Equal(string.Empty, LogRedactor.Redact(null));
    }
}

public sealed class FileAppLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gw2lt-log-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileAppLog CreateLog(int retentionDays = 7, long maxBytes = FileAppLog.DefaultMaxFileBytes) => new(_root, _time, retentionDays, maxBytes);

    private string TodayFile => Path.Combine(_root, "app-20261022.log");

    [Fact]
    public void Entries_have_a_utc_time_a_level_and_the_message()
    {
        var log = CreateLog();

        log.Info("Start");
        log.Warning("Let op");
        log.Failure("Mislukt");

        var lines = File.ReadAllLines(TodayFile);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("2026-10-22T09:30:00.0000000+00:00 [INF] Start", lines[0], StringComparison.Ordinal);
        Assert.Contains("[WRN] Let op", lines[1], StringComparison.Ordinal);
        Assert.Contains("[ERR] Mislukt", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_never_reach_the_file()
    {
        var log = CreateLog();

        log.Info($"key {TestData.Key} en Bearer geheimtoken en ?access_token=ABC123");
        log.Failure("mislukt", new InvalidOperationException($"fout met {TestData.Key} in C:\\Users\\Bo\\x"));

        var text = File.ReadAllText(TodayFile);
        Assert.DoesNotContain(TestData.Key, text, StringComparison.Ordinal);
        Assert.DoesNotContain("geheimtoken", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Users\\Bo", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_is_written_with_its_type_and_inner_exception()
    {
        var log = CreateLog();

        log.Failure("Synchronisatie", new InvalidOperationException("buiten", new IOException("binnen")));

        var text = File.ReadAllText(TodayFile);
        Assert.Contains("InvalidOperationException: buiten", text, StringComparison.Ordinal);
        Assert.Contains("IOException: binnen", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_stay_on_one_line_and_are_limited_in_length()
    {
        var log = CreateLog();

        log.Info("regel een\r\nregel twee");
        log.Info(new string('x', 10_000));

        var lines = File.ReadAllLines(TodayFile);
        Assert.Equal(2, lines.Length);
        Assert.Contains("regel een regel twee", lines[0], StringComparison.Ordinal);
        Assert.True(lines[1].Length < 4200);
    }

    [Fact]
    public void A_new_day_starts_a_new_file()
    {
        var log = CreateLog();
        log.Info("dag een");

        _time.Advance(TimeSpan.FromDays(1));
        log.Info("dag twee");

        Assert.Contains("dag een", File.ReadAllText(TodayFile), StringComparison.Ordinal);
        Assert.Contains("dag twee", File.ReadAllText(Path.Combine(_root, "app-20261023.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void Files_older_than_the_retention_period_are_removed_at_start()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app-20261001.log"), "oud");
        File.WriteAllText(Path.Combine(_root, "app-20261020.log"), "recent");
        File.WriteAllText(Path.Combine(_root, "notities.txt"), "niet van ons");
        File.WriteAllText(Path.Combine(_root, "app-geen-datum.log"), "onbekend");

        _ = CreateLog(retentionDays: 7);

        Assert.False(File.Exists(Path.Combine(_root, "app-20261001.log")));
        Assert.True(File.Exists(Path.Combine(_root, "app-20261020.log")));
        Assert.True(File.Exists(Path.Combine(_root, "notities.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "app-geen-datum.log")));
    }

    [Fact]
    public void A_full_file_gets_one_closing_line_and_then_stays_quiet()
    {
        var log = CreateLog(maxBytes: 300);

        for (var i = 0; i < 50; i++)
        {
            log.Info($"bericht {i} met wat opvulling om de grens te bereiken");
        }

        var text = File.ReadAllText(TodayFile);
        Assert.Single(text.Split('\n'), line => line.Contains("Logbestand vol", StringComparison.Ordinal));
        Assert.True(new FileInfo(TodayFile).Length < 1000);

        _time.Advance(TimeSpan.FromDays(1));
        log.Info("nieuwe dag");
        Assert.True(File.Exists(Path.Combine(_root, "app-20261023.log")));
    }

    [Fact]
    public async Task Writing_from_many_threads_loses_nothing()
    {
        var log = CreateLog();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => log.Info($"bericht-{i}"))));

        var lines = File.ReadAllLines(TodayFile);
        Assert.Equal(100, lines.Length);
        Assert.Equal(100, Enumerable.Range(0, 100).Count(i => lines.Any(l => l.EndsWith($"bericht-{i}", StringComparison.Ordinal))));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_never_throws()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "geblokkeerd");
        File.WriteAllText(blocker, "dit is een bestand, geen map");

        var log = new FileAppLog(Path.Combine(blocker, "logs"), _time);

        var exception = Record.Exception(() =>
        {
            log.Info("x");
            log.Warning("y");
            log.Failure("z", new InvalidOperationException("boem"));
        });

        Assert.Null(exception);
    }

    [Fact]
    public void The_folder_is_exposed_so_the_user_can_find_the_files()
    {
        Assert.Equal(_root, CreateLog().Directory);
    }

    [Fact]
    public void The_null_log_does_nothing_and_does_not_throw()
    {
        NullAppLog.Instance.Info("a");
        NullAppLog.Instance.Warning("b");
        NullAppLog.Instance.Failure("c", new InvalidOperationException());
    }
}
