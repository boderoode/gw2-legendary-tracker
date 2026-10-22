using Gw2LegendaryTracker.Core.Export;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.Tests;

public sealed class UserDataFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gw2lt-files-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_creates_folders_replaces_files_and_leaves_no_temporary_file()
    {
        var path = Path.Combine(_root, "deep", "export.json");

        await UserDataFiles.WriteAtomicAsync(path, "een");
        await UserDataFiles.WriteAtomicAsync(path, "twee");

        Assert.Equal("twee", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Written_files_are_utf8_without_a_byte_order_mark()
    {
        var path = Path.Combine(_root, "export.json");

        await UserDataFiles.WriteAtomicAsync(path, "é");

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0xC3, 0xA9], bytes);
    }

    [Fact]
    public async Task A_failed_write_keeps_the_previous_file_and_cleans_up()
    {
        var path = Path.Combine(_root, "export.json");
        await UserDataFiles.WriteAtomicAsync(path, "origineel");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UserDataFiles.WriteAtomicAsync(path, "nieuw", cts.Token));

        Assert.Equal("origineel", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Read_returns_the_text_of_an_existing_file()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "in.json");
        await File.WriteAllTextAsync(path, "{}");

        var result = await UserDataFiles.ReadAsync(path);

        Assert.Equal("{}", result.Text);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Read_explains_a_missing_file_without_throwing()
    {
        var result = await UserDataFiles.ReadAsync(Path.Combine(_root, "weg.json"));

        Assert.Null(result.Text);
        Assert.Contains("bestaat niet", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_refuses_files_that_are_too_large_without_loading_them()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "groot.json");
        await using (var stream = File.Create(path))
        {
            stream.SetLength(UserDataSerializer.MaxBytes + 1);
        }

        var result = await UserDataFiles.ReadAsync(path);

        Assert.Null(result.Text);
        Assert.Contains("te groot", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backups_are_named_by_time_and_only_the_newest_few_are_kept()
    {
        var folder = Path.Combine(_root, "backups");
        var start = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 8; i++)
        {
            await UserDataFiles.BackupAsync(folder, $"kopie {i}", start.AddMinutes(i), keep: 5);
        }

        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(5, files.Count);
        Assert.Equal("before-import-20261022-080300.json", files[0]);
        Assert.Equal("before-import-20261022-080700.json", files[^1]);
        Assert.Equal("kopie 7", await File.ReadAllTextAsync(Path.Combine(folder, files[^1]!)));
    }

    [Fact]
    public async Task Backup_never_removes_files_that_are_not_backups()
    {
        var folder = Path.Combine(_root, "backups");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "mijn-eigen-notitie.json"), "bewaren");

        await UserDataFiles.BackupAsync(folder, "x", DateTimeOffset.UnixEpoch, keep: 1);

        Assert.True(File.Exists(Path.Combine(folder, "mijn-eigen-notitie.json")));
    }
}
