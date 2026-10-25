using System.Globalization;
using Gw2LegendaryTracker.Core.Cosmetics;
using Gw2LegendaryTracker.Core.Gw2;
using Gw2LegendaryTracker.Infrastructure.Dat;
using Gw2LegendaryTracker.Infrastructure.Gw2;

// Spike for the 3D viewer research (docs/character-viewer-feasibility.md, phase A). Read-only: it never changes
// the archive and writes nothing. Nothing from the archive is copied or stored.
//
//   dotnet run --project tools/Gw2LegendaryTracker.DatSpike -- info  "C:\path\to\Gw2.dat"
//   dotnet run --project tools/Gw2LegendaryTracker.DatSpike -- icons "C:\path\to\Gw2.dat"
//   dotnet run --project tools/Gw2LegendaryTracker.DatSpike -- verify "C:\path\to\Gw2.dat" [number of files, default 500]
//
// info   checks that the container of the archive can be read and prints its size figures.
// icons  checks the idea that the icon id in the URL of an API skin is also a file id in the archive.
// verify decompresses a spread of files and checks that each one decodes to exactly the size it announces,
//        and counts what kinds of files came out. Nothing is stored.
// census counts the kinds of all files in the archive by reading the first bytes of each one.
// context shows the bytes around the icon id inside one file found by find, and which numbers nearby are file
//        ids of the archive (and of what kind), to see how a skin points at its model.
// grep   shows every place where a byte pattern (for example a 16-byte id of a content object) occurs in one
//        file, with the bytes around it and the archive file ids nearby.
// find   looks for the icon file id of a skin inside the content of the archive's files, to see which files
//        describe that skin. It only prints where the id was found.

const string Usage = """
    Gebruik:
      DatSpike info|icons <Gw2.dat>
      DatSpike verify <Gw2.dat> [aantal bestanden]
      DatSpike census <Gw2.dat>
      DatSpike find <Gw2.dat> "<naam van een skin>" [bestandssoort, bijvoorbeeld PF/cntc]
      DatSpike context <Gw2.dat> "<naam van een skin>" <itemnummer uit find> [venster in bytes, standaard 1024]
      DatSpike grep <Gw2.dat> <itemnummer> <bytes als hex, bijvoorbeeld BA7A6791DA7F914A898090DAD944CD7D> [venster in bytes, standaard 512]
    """;

if (args.Length < 2 || args[0] is not ("info" or "icons" or "verify" or "census" or "find" or "context" or "grep"))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var sample = 500;
if (args[0] == "verify" && args.Length == 3
    && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out sample) || sample is < 1 or > 100_000))
{
    Console.Error.WriteLine("Het aantal bestanden moet een getal tussen 1 en 100.000 zijn.");
    return 2;
}

if ((args[0] is "info" or "icons" or "census" && args.Length != 2)
    || (args[0] == "verify" && args.Length > 3)
    || (args[0] == "find" && args.Length is < 3 or > 4)
    || (args[0] == "context" && args.Length is < 4 or > 5)
    || (args[0] == "grep" && args.Length is < 4 or > 5))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    using var archive = DatArchive.Open(args[1]);
    return args[0] switch
    {
        "info" => Info(archive),
        "icons" => await IconsAsync(archive),
        "census" => Census(archive),
        "find" => await FindAsync(archive, args[2], args.Length == 4 ? args[3] : null),
        "context" => await ContextAsync(archive, args[2], args[3], args.Length == 5 ? args[4] : null),
        "grep" => Grep(archive, args[2], args[3], args.Length == 5 ? args[4] : null),
        _ => Verify(archive, sample),
    };
}
catch (DatFormatException ex)
{
    Console.Error.WriteLine($"Het archief kon niet worden gelezen: {ex.Message}");
    return 1;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"Het bestand kon niet worden geopend: {ex.Message}");
    return 1;
}
catch (Gw2ApiException ex)
{
    Console.Error.WriteLine($"De Guild Wars 2-API gaf een fout: {ex.Message}");
    return 1;
}

static int Info(DatArchive archive)
{
    var used = archive.Entries.Count(e => e.IsInUse);
    var compressed = archive.Entries.Count(e => e.IsInUse && e.IsCompressed);
    Console.WriteLine($"Bestandsgrootte:       {archive.FileSize / 1_048_576:N0} MiB");
    Console.WriteLine($"Versie (byte 0):       0x{archive.Header.Version:X2}");
    Console.WriteLine($"Chunkgrootte:          {archive.Header.ChunkSize}");
    Console.WriteLine($"Tabel op positie:      {archive.Header.MftOffset:N0}, {archive.Header.MftSize:N0} bytes");
    Console.WriteLine($"Items in de tabel:     {archive.Entries.Count:N0}");
    Console.WriteLine($"  in gebruik:          {used:N0}");
    Console.WriteLine($"  gecomprimeerd:       {compressed:N0}");
    Console.WriteLine($"Bekende bestands-id's: {archive.IdCount:N0}");
    Console.WriteLine("Het archief is gelezen; er is niets gewijzigd.");
    return 0;
}

static async Task<int> IconsAsync(DatArchive archive)
{
    using var http = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker-DatSpike/0.1");
    var skins = await new Gw2ApiClient(http).GetAllSkinsAsync(new Progress<int>(count => Console.Write($"\rSkins ophalen… {count:N0}")));
    Console.WriteLine();

    foreach (var group in skins.GroupBy(s => s.Category).OrderBy(g => g.Key))
    {
        var withId = 0;
        var found = 0;
        var inUse = 0;
        var missing = new List<string>();
        foreach (var skin in group)
        {
            if (IconFileId.TryParse(skin.IconUrl) is not { } id)
            {
                continue;
            }

            withId++;
            if (archive.FindEntry(id) is { } index)
            {
                found++;
                if (archive.Entries[index].IsInUse)
                {
                    inUse++;
                }
            }
            else if (missing.Count < 3)
            {
                missing.Add($"{skin.Name} (icoon {id})");
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{group.Key,-10} skins: {group.Count(),6:N0}   met icoon-id: {withId,6:N0}   gevonden in archief: {found,6:N0}   in gebruik: {inUse,6:N0}"));
        foreach (var example in missing)
        {
            Console.WriteLine($"             niet gevonden, voorbeeld: {example}");
        }
    }

    return 0;
}

static int Verify(DatArchive archive, int sample)
{
    // Files above this stored size are skipped to keep memory use small; the share that was skipped is reported.
    const uint MaxStored = 8 * 1024 * 1024;

    var candidates = new List<int>();
    var skippedLarge = 0;
    for (var i = DatArchive.FirstFileEntry; i < archive.Entries.Count; i++)
    {
        var entry = archive.Entries[i];
        if (!entry.IsInUse || !entry.IsCompressed)
        {
            continue;
        }

        if (entry.Size > MaxStored)
        {
            skippedLarge++;
            continue;
        }

        candidates.Add(i);
    }

    var chosen = candidates.Count <= sample
        ? candidates
        : [.. Enumerable.Range(0, sample).Select(n => candidates[(int)((long)n * candidates.Count / sample)])];

    var kinds = new Dictionary<string, int>();
    var failures = new List<string>();
    long bytes = 0;
    var ok = 0;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    foreach (var index in chosen)
    {
        try
        {
            var data = archive.Read(index);
            bytes += data.Length;
            ok++;
            var kind = Describe(data);
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }
        catch (DatFormatException ex)
        {
            if (failures.Count < 5)
            {
                failures.Add($"item {index} (opgeslagen {archive.Entries[index].Size:N0} bytes): {ex.Message}");
            }
        }
    }

    watch.Stop();
    Console.WriteLine($"Gecomprimeerde bestanden in het archief: {candidates.Count + skippedLarge:N0} (groter dan 8 MiB overgeslagen: {skippedLarge:N0})");
    Console.WriteLine($"Gecontroleerd: {chosen.Count:N0}   goed uitgepakt: {ok:N0}   mislukt: {chosen.Count - ok:N0}");
    Console.WriteLine($"Uitgepakt: {bytes / 1_048_576.0:N1} MiB in {watch.Elapsed.TotalSeconds:N1} s");
    foreach (var failure in failures)
    {
        Console.WriteLine($"  mislukt: {failure}");
    }

    Console.WriteLine("Soorten bestanden (eerste bytes):");
    foreach (var (kind, count) in kinds.OrderByDescending(k => k.Value).Take(25))
    {
        Console.WriteLine($"  {count,6:N0}  {kind}");
    }

    return chosen.Count == ok ? 0 : 1;
}

static string Describe(byte[] data)
{
    if (data.Length >= 12 && data[0] == (byte)'P' && data[1] == (byte)'F')
    {
        return "PF/" + Printable(data.AsSpan(8, 4));
    }

    return data.Length >= 4 ? Printable(data.AsSpan(0, 4)) : "(korter dan 4 bytes)";
}

static string Printable(ReadOnlySpan<byte> bytes)
{
    foreach (var b in bytes)
    {
        if (b is < 0x20 or > 0x7E)
        {
            return "0x" + Convert.ToHexString(bytes);
        }
    }

    return System.Text.Encoding.ASCII.GetString(bytes);
}

static int Census(DatArchive archive)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var kinds = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, long Stored)>();
    var unreadable = 0;
    Parallel.For(DatArchive.FirstFileEntry, archive.Entries.Count, index =>
    {
        var entry = archive.Entries[index];
        if (!entry.IsInUse)
        {
            return;
        }

        string kind;
        try
        {
            kind = Describe(archive.ReadPrefix(index, 12));
        }
        catch (DatFormatException)
        {
            Interlocked.Increment(ref unreadable);
            return;
        }

        kinds.AddOrUpdate(kind, (1, entry.Size), (_, old) => (old.Count + 1, old.Stored + entry.Size));
    });

    watch.Stop();
    Console.WriteLine($"Gelezen in {watch.Elapsed.TotalSeconds:N0} s; niet te lezen: {unreadable:N0}; soorten: {kinds.Count:N0}");
    Console.WriteLine("Aantal  Opgeslagen (MiB)  Soort");
    foreach (var (kind, value) in kinds.OrderByDescending(k => k.Value.Count).Take(60))
    {
        Console.WriteLine($"{value.Count,7:N0}  {value.Stored / 1_048_576.0,15:N0}  {kind}");
    }

    return 0;
}

static async Task<int> FindAsync(DatArchive archive, string skinName, string? kindFilter)
{
    using var http = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker-DatSpike/0.1");
    var skins = await new Gw2ApiClient(http).GetAllSkinsAsync();
    var matches = skins.Where(s => string.Equals(s.Name, skinName, StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
    if (matches.Count == 0)
    {
        Console.Error.WriteLine($"Geen skin met de naam '{skinName}' gevonden. Gebruik de Engelse naam, precies zoals in het spel.");
        return 1;
    }

    var wanted = Wanted(archive, matches);

    // Textures, audio and the like never describe a skin and make up most of the archive, so they are skipped
    // unless a kind is asked for.
    static bool Skipped(string kind) => kind.StartsWith("AT", StringComparison.Ordinal) || kind.StartsWith("DDS", StringComparison.Ordinal)
        || kind is "PF/ASND" or "PF/ABNK" or "PF/MODL" or "PF/AMSP";

    const uint MaxStored = 8 * 1024 * 1024;
    var hits = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var scanned = 0;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var lastReport = 0L;
    Parallel.For(DatArchive.FirstFileEntry, archive.Entries.Count, index =>
    {
        var entry = archive.Entries[index];
        if (!entry.IsInUse || entry.Size > MaxStored || hits.Count >= 60)
        {
            return;
        }

        try
        {
            var kind = Describe(archive.ReadPrefix(index, 12));
            if (kindFilter is null ? Skipped(kind) : !kind.Equals(kindFilter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var data = archive.Read(index);
            Interlocked.Increment(ref scanned);
            foreach (var (skin, iconId, pattern) in wanted)
            {
                var offsets = new List<int>();
                for (var at = 0; offsets.Count < 5; )
                {
                    var found = data.AsSpan(at).IndexOf(pattern);
                    if (found < 0)
                    {
                        break;
                    }

                    offsets.Add(at + found);
                    at += found + 1;
                }

                if (offsets.Count > 0)
                {
                    var ids = string.Join(", ", archive.IdsOf(index));
                    hits.Enqueue($"item {index} (id's: {ids}), soort {kind}, {data.Length:N0} bytes: icoon {iconId} van '{skin.Name}' op positie {string.Join(", ", offsets)}");
                }
            }

            var now = watch.ElapsedMilliseconds;
            if (now - Interlocked.Read(ref lastReport) > 15_000 && Interlocked.Exchange(ref lastReport, now) != now)
            {
                Console.WriteLine($"  … {scanned:N0} bestanden bekeken, {hits.Count} treffers, {now / 1000} s");
            }
        }
        catch (DatFormatException)
        {
            // A file that does not decode is skipped; verify and census report those.
        }
    });

    watch.Stop();
    Console.WriteLine($"Bekeken: {scanned:N0} bestanden in {watch.Elapsed.TotalSeconds:N0} s. Treffers: {hits.Count}");
    foreach (var hit in hits.Take(60))
    {
        Console.WriteLine("  " + hit);
    }

    return 0;
}

/// <summary>The icon id of each skin plus the other ids of the same archive entry, since content may use either.</summary>
static List<(CosmeticSkin Skin, uint IconId, byte[] Pattern)> Wanted(DatArchive archive, IEnumerable<CosmeticSkin> skins)
{
    var wanted = new List<(CosmeticSkin, uint, byte[])>();
    foreach (var skin in skins)
    {
        if (IconFileId.TryParse(skin.IconUrl) is not { } iconId)
        {
            continue;
        }

        var ids = new SortedSet<uint> { iconId };
        if (archive.FindEntry(iconId) is { } entry)
        {
            ids.UnionWith(archive.IdsOf(entry));
        }

        Console.WriteLine($"Zoeken naar skin {skin.Id} '{skin.Name}' ({skin.Category} {skin.Slot}), icoon-id {iconId} (id's van dit bestand: {string.Join(", ", ids)})");
        wanted.AddRange(ids.Select(id => (skin, id, BitConverter.GetBytes(id))));
    }

    return wanted;
}

static async Task<int> ContextAsync(DatArchive archive, string skinName, string itemText, string? windowText)
{
    var window = 1024;
    if (!int.TryParse(itemText, NumberStyles.None, CultureInfo.InvariantCulture, out var item) || item < DatArchive.FirstFileEntry || item >= archive.Entries.Count
        || (windowText is not null && (!int.TryParse(windowText, NumberStyles.None, CultureInfo.InvariantCulture, out window) || window is < 64 or > 65_536)))
    {
        Console.Error.WriteLine("Het itemnummer moet uit de uitvoer van 'find' komen en het venster tussen 64 en 65.536 bytes liggen.");
        return 2;
    }

    using var http = new HttpClient { BaseAddress = new Uri("https://api.guildwars2.com/"), Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Gw2LegendaryTracker-DatSpike/0.1");
    var skins = await new Gw2ApiClient(http).GetAllSkinsAsync();
    var match = skins.FirstOrDefault(s => string.Equals(s.Name, skinName, StringComparison.OrdinalIgnoreCase));
    if (match is null)
    {
        Console.Error.WriteLine($"Geen skin met de naam '{skinName}' gevonden.");
        return 1;
    }

    var data = archive.Read(item);
    Console.WriteLine($"Bestand {item}: {data.Length:N0} bytes");
    var shown = 0;
    foreach (var (skin, id, pattern) in Wanted(archive, [match]))
    {
        for (var at = 0; shown < 4;)
        {
            var found = data.AsSpan(at).IndexOf(pattern);
            if (found < 0)
            {
                break;
            }

            var position = at + found;
            at = position + 1;
            shown++;
            Console.WriteLine();
            Console.WriteLine($"== id {id} ('{skin.Name}') op positie {position:N0} ==");
            HexDump(data, Math.Max(0, position - 128), Math.Min(data.Length, position + 128));
            ShowNeighbors(archive, data, position, window, id);
        }
    }

    return shown == 0 ? 1 : 0;
}

static void HexDump(byte[] data, int from, int to)
{
    for (var row = from & ~15; row < to; row += 16)
    {
        var hex = new System.Text.StringBuilder();
        var text = new System.Text.StringBuilder();
        for (var i = row; i < row + 16; i++)
        {
            if (i < from || i >= to)
            {
                hex.Append("   ");
                text.Append(' ');
                continue;
            }

            hex.Append(data[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            text.Append(data[i] is >= 0x20 and < 0x7F ? (char)data[i] : '.');
        }

        Console.WriteLine($"{row,10:X8}  {hex} {text}");
    }
}

/// <summary>Numbers near a position that are file ids of the archive, with their kind. Audio and similar noise is left out.</summary>
static void ShowNeighbors(DatArchive archive, byte[] data, int position, int window, uint skipValue)
{
    Console.WriteLine($"-- getallen in de buurt (±{window} bytes) die bestands-id's van het archief zijn (geluid weggelaten) --");
    var from = Math.Max(0, position - window);
    var to = Math.Min(data.Length - 4, position + window);
    var listed = 0;
    for (var i = from; i <= to && listed < 60; i++)
    {
        var value = BitConverter.ToUInt32(data, i);
        if (value == skipValue || archive.FindEntry(value) is not { } entry)
        {
            continue;
        }

        string kind;
        try
        {
            kind = Describe(archive.ReadPrefix(entry, 12));
        }
        catch (DatFormatException)
        {
            kind = "(niet te lezen)";
        }

        if (kind is "PF/ASND" or "PF/ABNK" or "PF/AMSP" or "DDS " or "<!DO")
        {
            continue;
        }

        listed++;
        Console.WriteLine($"  {i - position,6:+#;-#;0} bytes: {value,10}  {kind}");
    }
}

static int Grep(DatArchive archive, string itemText, string hex, string? windowText)
{
    var window = 512;
    byte[] pattern;
    try
    {
        pattern = Convert.FromHexString(hex);
    }
    catch (FormatException)
    {
        pattern = [];
    }

    if (!int.TryParse(itemText, NumberStyles.None, CultureInfo.InvariantCulture, out var item) || item < DatArchive.FirstFileEntry || item >= archive.Entries.Count
        || pattern.Length is < 4 or > 64
        || (windowText is not null && (!int.TryParse(windowText, NumberStyles.None, CultureInfo.InvariantCulture, out window) || window is < 64 or > 65_536)))
    {
        Console.Error.WriteLine("Geef een itemnummer, 4 tot 64 bytes als hex (zonder spaties) en eventueel een venster tussen 64 en 65.536.");
        return 2;
    }

    var data = archive.Read(item);
    Console.WriteLine($"Bestand {item}: {data.Length:N0} bytes");
    var shown = 0;
    var total = 0;
    for (var at = 0; ;)
    {
        var found = data.AsSpan(at).IndexOf(pattern);
        if (found < 0)
        {
            break;
        }

        var position = at + found;
        at = position + 1;
        total++;
        if (shown >= 6)
        {
            continue;
        }

        shown++;
        Console.WriteLine();
        Console.WriteLine($"== treffer {shown} op positie {position:N0} (0x{position:X}) ==");
        HexDump(data, Math.Max(0, position - 96), Math.Min(data.Length, position + pattern.Length + 96));
        ShowNeighbors(archive, data, position, window, 0);
    }

    Console.WriteLine();
    Console.WriteLine($"Aantal plekken in dit bestand: {total:N0}{(total > shown ? $" (de eerste {shown} zijn getoond)" : string.Empty)}");
    return total == 0 ? 1 : 0;
}
