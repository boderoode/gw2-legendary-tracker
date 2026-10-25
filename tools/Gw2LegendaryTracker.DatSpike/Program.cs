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
//
// info   checks that the container of the archive can be read and prints its size figures.
// icons  checks the idea that the icon id in the URL of an API skin is also a file id in the archive.

if (args.Length != 2 || args[0] is not ("info" or "icons"))
{
    Console.Error.WriteLine("Gebruik: DatSpike info|icons <pad naar Gw2.dat>");
    return 2;
}

try
{
    using var archive = DatArchive.Open(args[1]);
    return args[0] == "info" ? Info(archive) : await IconsAsync(archive);
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
