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
| 6 | Crafting-engine en materiaalberekeningen | Klaar (engine en data; nog geen scherm) |
| 7 | Legendary-doelen en handmatige progress | Klaar, UI **nog niet getest op Windows** |
| 8 | Compacte en uitgebreide overlay | Gedeeltelijk: de overlay toont al naam, voortgang, volgende stap en tekorten |
| 9 | Export/import, foutafhandeling, extra tests | Gepland |
| 10 | Reproduceerbare Windows-build en installer | Gepland |

## Crafting-engine en receptdata

De engine rekent een crafting tree recursief door: eerst gebruikt hij wat je bezit, daarna craft hij de rest.
Hij bepaalt tekorten, kiest bij meerdere recepten de beste route, en wijst de eerstvolgende stap aan.
Meerdere doelen delen één voorraad; een doel eerder in de lijst heeft voorrang.

- **Voortgang** is het aandeel posities in de crafting tree dat klaar is: je hebt er genoeg van, of je kunt het
  helemaal uit je bezit maken. 100% betekent dat je het doel nu kunt afronden.
- **Beste route:** de route met de minste ontbrekende stuks, daarna de minste craftacties, daarna het laagste receptnummer.
  Je kunt een route altijd zelf forceren.
- **Receptdata** komt uit drie bronnen, die samen één receptboek vormen:
  1. de officiële API: alle ~13.200 gewone recepten, eenmalig gedownload en lokaal bewaard (de eerste keer duurt
     dat ongeveer twee minuten);
  2. gw2treasures, voor de Mystic Forge-recepten van Legendaries (zie hieronder);
  3. lokale bestanden in `data\recipes` (meegeleverd) en `%LOCALAPPDATA%\Gw2LegendaryTracker\recipes` (van jezelf).

### Belangrijke beperking: Legendary-recepten

De API bevat de onderdelen van Legendaries, maar niet de Legendaries zelf. Van de 206 Legendaries heeft de API alleen
recepten voor de 18 Obsidian-harnasstukken; de Legendary-wapens, Gifts en Mystic Forge-stappen ontbreken, net als
currency-kosten (recepten in de API kennen alleen items). Die stappen moeten uit **lokale receptdata** komen:

```json
{ "recipes": [ { "id": -1, "output": { "itemId": 100, "count": 1 },
                 "ingredients": [ { "itemId": 5, "count": 2 }, { "currencyId": 23, "count": 250 } ] } ] }
```

Ids van lokale recepten zijn negatief, zodat ze nooit botsen met API-recepten. Ongeldige regels worden overgeslagen en
gemeld; de rest van het bestand blijft werken. Recepten die guild-upgrades vereisen worden niet gebruikt.
Receptbestanden worden gelezen uit `data\recipes` naast het programma en uit `%LOCALAPPDATA%\Gw2LegendaryTracker\recipes`.

### Recepten van een Legendary ophalen (gw2treasures)

Zodra je een Legendary kiest, haalt de app de Mystic Forge-recepten van die Legendary en van al zijn onderdelen op
bij de API van gw2treasures. Daarvoor is een gratis key nodig die je zelf aanmaakt (de stappen staan in het scherm).

- **Per onderdeel, met cache:** het endpoint geeft één item per aanroep, dus de app loopt de boom af en onthoudt elk
  onderdeel zeven dagen. Gedeelde onderdelen (Gift of Fortune, Gift of Mastery, ...) worden maar één keer opgehaald:
  Twilight kostte 13 aanroepen, Bifrost daarna 6. Zonder internet blijven opgeslagen recepten werken.
- **Rustig:** minstens 1,6 seconde tussen aanroepen en hooguit 150 per keer. Ruwe materialen worden niet opgezocht.
- **Controle:** elk recept wordt vergeleken met de officiële itembeschrijving ("Made by combining these items in the
  Mystic Forge"). Wijkt het af, dan wordt het niet gebruikt en meldt de app dat. Staat er geen beschrijving, dan wordt
  het recept bewaard maar gemeld als "één bron, niet gecontroleerd".
- **Lijst van Legendaries:** `/v2/legendaryarmory`, hooguit eens per 24 uur ververst.
- De key staat versleuteld (Windows DPAPI) in `%LOCALAPPDATA%\Gw2LegendaryTracker\forgekey.bin`, nooit in de
  database of in logs. Het kan zijn dat gw2treasures dit endpoint later alleen na goedkeuring van je applicatie
  openstelt; de app meldt dat dan en gebruikt wat is opgeslagen.

Het bestand `data/recipes/legendary.json` (Twilight) is een terugval en testgeval. Het kan met een hulpprogramma
opnieuw worden gegenereerd en gecontroleerd; dat is alleen voor ontwikkelaars:

```powershell
$env:GW2TREASURES_KEY = "<key van api.gw2treasures.com>"
dotnet run --project tools/Gw2LegendaryTracker.PackBuilder -- --root 30704   # 30704 = Twilight; herhaal --root voor meer
```

Het hulpprogramma schrijft niets als een id of naam niet klopt met de officiële API of een recept de officiële
beschrijving tegenspreekt. De data komt van gw2treasures, een community-project. Controleer hun voorwaarden voordat je
de app of dit bestand deelt buiten privégebruik.

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
