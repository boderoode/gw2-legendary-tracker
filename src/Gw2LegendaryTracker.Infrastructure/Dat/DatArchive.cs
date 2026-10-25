using System.Buffers.Binary;

namespace Gw2LegendaryTracker.Infrastructure.Dat;

public sealed class DatFormatException : Exception
{
    public DatFormatException(string message)
        : base(message)
    {
    }
}

public sealed record DatHeader(byte Version, uint ChunkSize, ulong MftOffset, uint MftSize);

/// <summary>One slot of the master file table. <see cref="Size"/> is the stored size, which may be compressed.</summary>
public readonly record struct DatEntry(ulong Offset, uint Size, ushort CompressionFlag, ushort EntryFlags)
{
    private const ushort Compressed = 0x08;
    private const ushort InUse = 0x01;

    public bool IsCompressed => (CompressionFlag & Compressed) != 0;

    public bool IsInUse => (EntryFlags & InUse) != 0;
}

/// <summary>
/// Read-only access to the container of a Gw2.dat: header, master file table and the table that maps file ids to
/// table entries. It does not read file contents; that needs the decompressor, which is a separate step.
/// The file is opened with sharing allowed, so it also works while the game is running.
/// </summary>
public sealed class DatArchive : IDisposable
{
    private const int HeaderSize = 40;
    private const int EntrySize = 24;

    /// <summary>The first entries describe the archive itself, not game files.</summary>
    public const int FirstFileEntry = 16;

    private const int IdTableEntry = 2;

    private static readonly byte[] ArchiveSignature = [(byte)'A', (byte)'N', 0x1A];
    private static readonly byte[] MftSignature = [(byte)'M', (byte)'f', (byte)'t', 0x1A];

    private readonly FileStream _file;
    private readonly Dictionary<uint, int> _entryById;

    private DatArchive(FileStream file, DatHeader header, DatEntry[] entries, Dictionary<uint, int> entryById)
    {
        _file = file;
        Header = header;
        Entries = entries;
        _entryById = entryById;
    }

    public DatHeader Header { get; }

    public IReadOnlyList<DatEntry> Entries { get; }

    /// <summary>How many file ids point at an entry.</summary>
    public int IdCount => _entryById.Count;

    public long FileSize => _file.Length;

    public static DatArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
        try
        {
            return Read(file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>The index of the entry for a file id, or null when the archive does not know the id.</summary>
    public int? FindEntry(uint fileId) => _entryById.TryGetValue(fileId, out var index) ? index : null;

    /// <summary>The bytes as stored, still compressed and with checksums.</summary>
    public byte[] ReadStored(int index)
    {
        if ((uint)index >= (uint)Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var entry = Entries[index];
        if (!entry.IsInUse)
        {
            throw new DatFormatException($"Entry {index} is not in use.");
        }

        return ReadExactly(entry.Offset, checked((int)entry.Size), $"Entry {index}");
    }

    public void Dispose() => _file.Dispose();

    private static DatArchive Read(FileStream file)
    {
        if (file.Length < HeaderSize)
        {
            throw new DatFormatException("Het bestand is te klein om een Gw2.dat te zijn.");
        }

        var raw = ReadAt(file, 0, HeaderSize, "header");
        if (!raw.AsSpan(1, 3).SequenceEqual(ArchiveSignature))
        {
            throw new DatFormatException("Dit is geen Guild Wars 2-archief: de kenmerken 'AN' ontbreken.");
        }

        var header = new DatHeader(
            raw[0],
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12)),
            BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(24)),
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(32)));

        if (header.MftOffset > (ulong)file.Length || header.MftSize > (ulong)file.Length - header.MftOffset)
        {
            throw new DatFormatException("De bestandstabel (MFT) ligt buiten het bestand.");
        }

        if (header.MftSize % EntrySize != 0 || header.MftSize < EntrySize * FirstFileEntry)
        {
            throw new DatFormatException("De bestandstabel (MFT) heeft een ongeldige grootte.");
        }

        var mft = ReadAt(file, (long)header.MftOffset, (int)header.MftSize, "MFT");
        if (!mft.AsSpan(0, 4).SequenceEqual(MftSignature))
        {
            throw new DatFormatException("De bestandstabel (MFT) heeft geen geldige kenmerken.");
        }

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(mft.AsSpan(12));
        if ((ulong)declared * EntrySize != header.MftSize)
        {
            throw new DatFormatException("Het aantal items in de bestandstabel klopt niet met de grootte.");
        }

        var entries = new DatEntry[mft.Length / EntrySize];
        for (var i = 0; i < entries.Length; i++)
        {
            var span = mft.AsSpan(i * EntrySize, EntrySize);
            entries[i] = new DatEntry(
                BinaryPrimitives.ReadUInt64LittleEndian(span),
                BinaryPrimitives.ReadUInt32LittleEndian(span[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[12..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[14..]));
        }

        return new DatArchive(file, header, entries, ReadIdTable(file, entries));
    }

    /// <summary>The id table is a plain list of (file id, entry index) pairs stored in one of the first entries.</summary>
    private static Dictionary<uint, int> ReadIdTable(FileStream file, DatEntry[] entries)
    {
        var entry = entries[IdTableEntry];
        if (!entry.IsInUse || entry.Offset > (ulong)file.Length || entry.Size > (ulong)file.Length - entry.Offset)
        {
            throw new DatFormatException("De tabel met bestands-id's ontbreekt of ligt buiten het bestand.");
        }

        var table = ReadAt(file, (long)entry.Offset, (int)entry.Size, "id-tabel");
        var byId = new Dictionary<uint, int>(table.Length / 8);
        for (var at = 0; at + 8 <= table.Length; at += 8)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(at));
            var index = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(at + 4));
            if ((id == 0 && index == 0) || index >= entries.Length)
            {
                continue;
            }

            byId[id] = (int)index;
        }

        return byId;
    }

    private byte[] ReadExactly(ulong offset, int length, string what)
    {
        if (offset > (ulong)_file.Length || (ulong)length > (ulong)_file.Length - offset)
        {
            throw new DatFormatException($"{what} ligt buiten het bestand.");
        }

        return ReadAt(_file, (long)offset, length, what);
    }

    private static byte[] ReadAt(FileStream file, long offset, int length, string what)
    {
        var buffer = new byte[length];
        try
        {
            // A read may return fewer bytes than asked, so keep reading until the buffer is full.
            var done = 0;
            while (done < length)
            {
                var read = RandomAccess.Read(file.SafeFileHandle, buffer.AsSpan(done), offset + done);
                if (read == 0)
                {
                    throw new DatFormatException($"De {what} is afgekapt: het bestand is korter dan verwacht.");
                }

                done += read;
            }
        }
        catch (IOException ex)
        {
            throw new DatFormatException($"De {what} kon niet worden gelezen: {ex.Message}");
        }

        return buffer;
    }
}
