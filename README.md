# GW2 Legendary Tracker

Een Windows-desktopapp (WPF, .NET 10) om je Legendary crafting progress in Guild Wars 2 bij te houden.
Eén applicatie met twee weergaven: een normaal **hoofdvenster** en een compacte **overlay** boven de game.
Beide gebruiken dezelfde database, instellingen en logica.

## Status

Dit project wordt in fases opgebouwd. Een fase is pas klaar als de build en tests groen zijn.

| Fase | Onderdeel | Status |
| --- | --- | --- |
| 1 | Desktopapp en hoofdvenster | Klaar |
| 2 | Overlay met toggle-sneltoets | Klaar, **nog niet getest op Windows** |
| 3 | Lokale opslag en gedeelde applicatiestatus | Klaar |
| 4 | API-keyvalidatie en synchronisatie | Klaar, UI **nog niet getest op Windows** |
| 5 | Inventorymodel | Klaar, UI **nog niet getest op Windows** |
| 6 | Crafting-engine en materiaalberekeningen | Gepland |
| 7 | Legendary-doelen en handmatige progress | Gepland |
| 8 | Compacte en uitgebreide overlay | Gepland |
| 9 | Export/import, foutafhandeling, extra tests | Gepland |
| 10 | Reproduceerbare Windows-build en installer | Gepland |

## Bekende beperkingen van de overlay

- De overlay is een gewoon Windows-venster dat `Topmost` is. Dat werkt voor **windowed** en **borderless fullscreen**.
- **Exclusive fullscreen wordt niet ondersteund** en is niet getest. Zet Guild Wars 2 op *Windowed Fullscreen*.
- Het project is ontwikkeld op macOS. De UI en overlay zijn daar gecompileerd, maar niet uitgevoerd.
  Het testen op Windows (schermmodi, meerdere monitoren, DPI) staat nog open.
- De overlay leest niets uit de gameclient: geen DLL-injectie, geen geheugenuitlezing, geen automatisering.

## Ontwikkelen

Vereisten: .NET SDK 10 (zie `global.json`). Bouwen kan ook op macOS/Linux (`EnableWindowsTargeting`), starten alleen op Windows.

```powershell
dotnet build
dotnet test
dotnet run --project src/Gw2LegendaryTracker.App
```

Zelfstandige publicatie:

```powershell
dotnet publish src/Gw2LegendaryTracker.App -c Release -r win-x64 --self-contained -o artifacts/win-x64
```

## Structuur

```
src/Gw2LegendaryTracker.Core            Domeinmodellen, instellingen, gedeelde AppState (geen UI, geen I/O)
src/Gw2LegendaryTracker.Infrastructure  SQLite-opslag en migraties
src/Gw2LegendaryTracker.App             WPF: hoofdvenster, overlay, sneltoets
tests/Gw2LegendaryTracker.Tests         xUnit
```

## API-key instellen

1. Ga naar <https://account.arena.net/applications> en log in.
2. Klik op **New Key** en geef de key een naam.
3. Vink alleen deze rechten aan: **account**, **inventories**, **characters**, **wallet**.
4. Kopieer de key, plak hem in het hoofdvenster en klik op **Verbinden**.

De app controleert de key via `/v2/tokeninfo` en weigert hem als er rechten ontbreken.
De app gebruikt de key uitsluitend voor leesverzoeken. Je kunt de key altijd verwijderen in het hoofdvenster
of intrekken op de ArenaNet-site.

## Lokale opslag

De database staat in `%LOCALAPPDATA%\Gw2LegendaryTracker\tracker.db`. Het schema is geversioneerd
(`PRAGMA user_version`) en wordt bij het starten gemigreerd; bestaande gegevens blijven behouden.
De API-key staat **niet** in de database, in instellingen of in logs. Hij wordt versleuteld met Windows DPAPI
(huidige Windows-gebruiker) opgeslagen in `%LOCALAPPDATA%\Gw2LegendaryTracker\apikey.bin`; alleen jouw
Windows-account op deze computer kan hem ontsleutelen. Zonder internet blijft de laatst opgehaalde
inventaris beschikbaar. Namen en iconen van items worden uit de publieke API gehaald (zonder key) en lokaal
bewaard in een aparte cachetabel; die kan veilig worden gewist.
