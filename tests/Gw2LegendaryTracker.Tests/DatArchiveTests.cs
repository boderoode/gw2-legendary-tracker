using System.Buffers.Binary;
using Gw2LegendaryTracker.Core.Cosmetics;
using Gw2LegendaryTracker.Infrastructure.Dat;

namespace Gw2LegendaryTracker.Tests;

/// <summary>Builds a tiny archive with the real container layout, so the reader is tested without a game installation.</summary>
internal sealed class SyntheticDat : IDisposable
{
    private const int HeaderSize = 40;
    private const int EntrySize = 24;

    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gw2lt-dat-" + Guid.NewGuid().ToString("N"));

    public SyntheticDat(IReadOnlyList<(uint Id, int Entry)> ids, int entryCount = 20, Action<byte[]>? tamper = null)
    {
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "Gw2.dat");

        var idTable = new byte[ids.Count * 8];
        for (var i = 0; i < ids.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(idTable.AsSpan(i * 8), ids[i].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(idTable.AsSpan((i * 8) + 4), (uint)ids[i].Entry);
        }

        var mftOffset = HeaderSize + idTable.Length;
        var mftSize = entryCount * EntrySize;
        var file = new byte[mftOffset + mftSize];

        file[0] = 0x97;
        file[1] = (byte)'A';
        file[2] = (byte)'N';
        file[3] = 0x1A;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 512);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(24), (ulong)mftOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32), (uint)mftSize);

        idTable.CopyTo(file, HeaderSize);

        "Mft\u001a"u8.CopyTo(file.AsSpan(mftOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(mftOffset + 12), (uint)entryCount);

        // Entry 2 is the id table; entry 5 is a compressed file; entry 6 is unused.
        WriteEntry(file, mftOffset, 2, (ulong)HeaderSize, (uint)idTable.Length, 0, 1);
        WriteEntry(file, mftOffset, 5, (ulong)HeaderSize, 8, 8, 1);
        WriteEntry(file, mftOffset, 6, (ulong)HeaderSize, 8, 0, 0);

        tamper?.Invoke(file);
        File.WriteAllBytes(Path, file);
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static void WriteEntry(byte[] file, int mftOffset, int index, ulong offset, uint size, ushort compression, ushort flags)
    {
        var span = file.AsSpan(mftOffset + (index * EntrySize));
        BinaryPrimitives.WriteUInt64LittleEndian(span, offset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], size);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], compression);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], flags);
    }
}

public class DatArchiveTests
{
    private static readonly (uint, int)[] Ids = [(61489, 5), (100, 17), (0, 0), (7, 9999)];

    [Fact]
    public void The_header_the_entries_and_the_id_table_are_read()
    {
        using var dat = new SyntheticDat(Ids);

        using var archive = DatArchive.Open(dat.Path);

        Assert.Equal(0x97, archive.Header.Version);
        Assert.Equal(512u, archive.Header.ChunkSize);
        Assert.Equal(20, archive.Entries.Count);
        Assert.Equal(2, archive.IdCount);
        Assert.Equal(5, archive.FindEntry(61489));
        Assert.Equal(17, archive.FindEntry(100));
    }

    [Fact]
    public void Unknown_ids_empty_pairs_and_entries_outside_the_table_are_ignored()
    {
        using var dat = new SyntheticDat(Ids);
        using var archive = DatArchive.Open(dat.Path);

        Assert.Null(archive.FindEntry(7));
        Assert.Null(archive.FindEntry(0));
        Assert.Null(archive.FindEntry(424242));
    }

    [Fact]
    public void Entry_flags_say_whether_an_entry_is_in_use_and_compressed()
    {
        using var dat = new SyntheticDat(Ids);
        using var archive = DatArchive.Open(dat.Path);

        Assert.True(archive.Entries[5].IsInUse);
        Assert.True(archive.Entries[5].IsCompressed);
        Assert.False(archive.Entries[6].IsInUse);
        Assert.False(archive.Entries[2].IsCompressed);
    }

    [Fact]
    public void Stored_bytes_are_returned_as_they_are_and_unused_or_missing_entries_are_refused()
    {
        using var dat = new SyntheticDat(Ids);
        using var archive = DatArchive.Open(dat.Path);

        Assert.Equal(8, archive.ReadStored(5).Length);
        Assert.Throws<DatFormatException>(() => archive.ReadStored(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadStored(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.ReadStored(20));
    }

    [Fact]
    public void The_archive_can_be_read_while_another_process_has_it_open_like_the_game_does()
    {
        using var dat = new SyntheticDat(Ids);
        using var holder = new FileStream(dat.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        using var archive = DatArchive.Open(dat.Path);

        Assert.Equal(5, archive.FindEntry(61489));
    }

    [Fact]
    public void A_file_that_is_not_an_archive_gives_a_clear_error()
    {
        using var dat = new SyntheticDat(Ids, tamper: file => file[1] = (byte)'X');

        var ex = Assert.Throws<DatFormatException>(() => DatArchive.Open(dat.Path));

        Assert.Contains("AN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_too_small_gives_a_clear_error()
    {
        using var dat = new SyntheticDat(Ids, tamper: file => Array.Clear(file));
        File.WriteAllBytes(dat.Path, new byte[10]);

        Assert.Throws<DatFormatException>(() => DatArchive.Open(dat.Path));
    }

    [Fact]
    public void A_table_that_lies_outside_the_file_is_refused()
    {
        using var dat = new SyntheticDat(Ids, tamper: file => BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(24), 1_000_000));

        Assert.Throws<DatFormatException>(() => DatArchive.Open(dat.Path));
    }

    [Fact]
    public void A_table_with_the_wrong_signature_or_count_is_refused()
    {
        using var badSignature = new SyntheticDat(Ids, tamper: file => file[BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(24))] = (byte)'X');
        using var badCount = new SyntheticDat(Ids, tamper: file => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(24)) + 12), 7));

        Assert.Throws<DatFormatException>(() => DatArchive.Open(badSignature.Path));
        Assert.Throws<DatFormatException>(() => DatArchive.Open(badCount.Path));
    }

    [Fact]
    public void A_table_of_the_wrong_size_is_refused()
    {
        using var dat = new SyntheticDat(Ids, tamper: file => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32), 25));

        Assert.Throws<DatFormatException>(() => DatArchive.Open(dat.Path));
    }

    [Fact]
    public void A_missing_file_reports_the_usual_io_error()
    {
        Assert.ThrowsAny<IOException>(() => DatArchive.Open(Path.Combine(Path.GetTempPath(), "gw2lt-missing", "Gw2.dat")));
    }
}

public class IconFileIdTests
{
    [Theory]
    [InlineData("https://render.guildwars2.com/file/1CD771E26EF70E493D00E20CA4D6D694AC630267/61489.png", 61489u)]
    [InlineData("https://render.guildwars2.com/file/ABC/102491.jpg", 102491u)]
    public void The_file_id_is_the_last_part_of_a_render_url(string url, uint expected) =>
        Assert.Equal(expected, IconFileId.TryParse(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://render.guildwars2.com/file/ABC/name.png")]
    [InlineData("https://render.guildwars2.com/file/ABC/0.png")]
    [InlineData("https://render.guildwars2.com/file/ABC/-5.png")]
    [InlineData("https://render.guildwars2.com/file/ABC/99999999999.png")]
    public void Anything_else_gives_no_id(string? url) => Assert.Null(IconFileId.TryParse(url));
}
