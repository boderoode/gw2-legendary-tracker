# GW2 Legendary Tracker

Een Windows-app om je Legendary crafting progress in Guild Wars 2 bij te houden. Eén programma met twee weergaven:
een gewoon **hoofdvenster** en een compacte **overlay** die boven het spel kan blijven staan. Beide gebruiken dezelfde
gegevens en dezelfde berekeningen, dus een wijziging in het ene venster is meteen zichtbaar in het andere.

De app leest alleen mee via de officiële Guild Wars 2 API. Er wordt niets in het spel gewijzigd: geen DLL-injectie,
geen geheugen uitlezen, geen automatisering.

## Installeren

Je hebt Windows 10 (versie 1809) of nieuwer nodig, 64-bit. Je hoeft niets anders te installeren; .NET zit erbij.

**Met de installer (aanbevolen)**
1. Download `Gw2LegendaryTracker-Setup-<versie>.exe` van de [releases](../../releases).
2. Start hem. Hij installeert voor jouw gebruiker, zonder beheerdersrechten, en maakt een snelkoppeling in het startmenu.
3. Verwijderen kan via *Instellingen → Apps*. De uninstaller vraagt of je ook je gegevens wilt verwijderen.

**Of als zipbestand**
1. Download `Gw2LegendaryTracker-<versie>-win-x64.zip` van de releases en pak het uit in een eigen map.
2. Start `Gw2LegendaryTracker.exe`. Verplaatsen kan door de hele map te kopiëren.

**Windows zegt dat het programma niet vertrouwd wordt.** De app is niet digitaal ondertekend (dat vraagt een betaald
certificaat). Klik op *Meer informatie* en dan op *Toch uitvoeren*. Wil je zeker zijn dat het bestand ongewijzigd is,
vergelijk dan de SHA256-waarde met `SHA256SUMS.txt` bij de release (`Get-FileHash <bestand>` in PowerShell).

## Eerste gebruik

1. **Verbind je account.** Maak op <https://account.arena.net/applications> een key aan met alleen de rechten
   **account**, **inventories**, **characters** en **wallet**. Plak hem in het hoofdvenster en klik op *Verbinden*.
   De app controleert de key en weigert hem als er rechten ontbreken.
2. **Voeg de gw2treasures-key toe** voor de recepten van de Legendaries zelf (uitleg staat in het scherm, zie ook
   [Recepten](#recepten)).
3. **Kies een Legendary** onder *Mijn Legendaries*. De volgorde bepaalt wie eerst jouw materialen krijgt.
4. De eerste start downloadt ongeveer 13.000 recepten; dat duurt enkele minuten en je ziet de voortgang.
5. Start Guild Wars 2 in **Windowed** of **Windowed Fullscreen** en toon de overlay met **Ctrl+Alt+L**.

## De overlay

- **Compact** (standaard): icoon en naam van de gekozen Legendary, voortgang, de volgende stap, de grootste tekorten
  als "heb X · nog Y" en het tijdstip van de laatste synchronisatie. Het aantal materialen stel je zelf in (1 tot 20).
- **Uitgebreid** (▾): volledige materiaallijst met filter voor alleen ontbrekende materialen, uitklapbare crafting
  tree, afvinklijst, currencies en reserveringen. Het venster past zijn hoogte aan.
- **Bediening:** slepen aan de kop, vergrendelen tegen slepen, transparantie, automatisch verbergen, en een
  *click-through*-stand waarin muisklikken door de overlay heen naar het spel gaan. De sneltoets en alle instellingen
  staan in het hoofdvenster, zodat je de bediening nooit kwijtraakt.
- **Focus:** een klik op de overlay neemt de focus van het spel niet over.
- **Voortgang** is het aandeel onderdelen in de crafting tree dat klaar is: je hebt er genoeg van, of je kunt het nu
  zelf maken met wat je bezit. 100% betekent dat je het doel nu kunt afronden.
- **Reserveringen** zijn geen aparte aantallen: de volgorde van je Legendaries bepaalt wie eerst materialen krijgt. De
  uitgebreide weergave toont wat Legendaries hoger in de lijst al van jouw voorraad gebruiken.

### Bekende beperkingen van de overlay

- De overlay is een gewoon Windows-venster dat altijd bovenaan blijft. Dat werkt boven **windowed** en **borderless
  fullscreen**. **Exclusive fullscreen wordt niet ondersteund** en is niet getest; kies *Windowed Fullscreen*.
- Het gedrag boven het spel (schermmodi, meerdere monitoren, verschillende schaling, focus) is nog niet systematisch
  gemeten. Het testplan staat in [`docs/overlay-test-plan.md`](docs/overlay-test-plan.md).
- De overlay leest niets uit de gameclient: alles komt uit de API, de recepten en wat je zelf afvinkt.

## Wat de app wel en niet telt

De voorraad bestaat uit je **bank, materials, gedeelde inventaris en de tassen van al je personages**. Niet geteld
worden uitgerust gear, de Trading Post, de guildbank en andere plekken die de inventory-endpoints niet geven. Een
onderdeel dat je zelf hebt gemaakt of gekregen maar dat de app niet ziet, kun je afvinken; alles eronder valt dan weg.

## Recepten

Het receptboek komt uit drie bronnen:

1. **De officiële API:** alle ongeveer 13.200 gewone recepten, eenmalig gedownload en lokaal bewaard.
2. **gw2treasures:** de Mystic Forge-recepten van Legendaries en hun onderdelen, die de officiële API niet bevat. Van
   de 206 Legendaries heeft de officiële API alleen recepten voor de 18 Obsidian-harnasstukken.
3. **Lokale bestanden** in `data\recipes` (meegeleverd, Twilight) en `%LOCALAPPDATA%\Gw2LegendaryTracker\recipes`
   (van jezelf) voor wat nergens anders staat, ook currency-kosten.

**Ophalen bij gw2treasures.** Zodra je een Legendary kiest, haalt de app de recepten van die Legendary en van al zijn
onderdelen op. Daarvoor is een gratis key nodig die je zelf aanmaakt (de stappen staan in het scherm).

- Het endpoint geeft één item per aanroep; de app loopt de boom af en onthoudt elk onderdeel zeven dagen. Gedeelde
  onderdelen (Gift of Fortune, Gift of Mastery, ...) worden één keer opgehaald: Twilight kostte 13 aanroepen, Bifrost
  daarna 6. Zonder internet blijven opgeslagen recepten werken.
- Minstens 1,6 seconde tussen aanroepen en hooguit 150 per keer. Ruwe materialen worden niet opgezocht.
- Elk recept wordt vergeleken met de officiële itembeschrijving ("Made by combining these items in the Mystic Forge").
  Wijkt het af, dan wordt het niet gebruikt en meldt de app dat. Staat er geen beschrijving, dan wordt het recept
  bewaard maar gemeld als "één bron, niet gecontroleerd".
- gw2treasures kan dit endpoint later alleen na goedkeuring van je applicatie openstellen; de app meldt dat dan en
  gebruikt wat is opgeslagen.
- De data komt van gw2treasures, een community-project. Controleer hun voorwaarden voordat je de app of de
  receptbestanden deelt buiten privégebruik.

**Berekening.** De engine gebruikt eerst wat je bezit en rekent de rest recursief door. Hij hergebruikt overschot, kiest
bij meerdere recepten de route met de minste ontbrekende stuks (daarna de minste craftacties, daarna het laagste
receptnummer) en wijst de eerstvolgende stap aan. Recepten die guild-upgrades vereisen en recepten die je eerst moet
leren worden niet meegerekend.

**Eigen receptbestanden** hebben dit formaat (ids zijn negatief zodat ze nooit botsen met API-recepten; ongeldige regels
worden overgeslagen en gemeld):

```json
{ "recipes": [ { "id": -1, "output": { "itemId": 100, "count": 1, "name": "Gift of Might" },
                 "ingredients": [ { "itemId": 5, "count": 2, "name": "Some Item" }, { "currencyId": 23, "count": 250 } ] } ] }
```

## Gegevens, privacy en beveiliging

**Netwerkverkeer.** De app praat met `api.guildwars2.com` (je account met jouw key, plus publieke lijsten),
`api.gw2treasures.com` (recepten, met jouw gw2treasures-key) en `render.guildwars2.com` (itemiconen). Er is geen
telemetrie, geen analyse en geen andere server. De GW2-key wordt alleen gebruikt voor leesverzoeken.

**Opslag** in `%LOCALAPPDATA%\Gw2LegendaryTracker\`:

| Bestand of map | Inhoud |
| --- | --- |
| `tracker.db` | SQLite: instellingen, doelen, afvinkingen, notities, laatste inventaris, gecachete itemnamen en recepten. Het schema is geversioneerd; updates migreren bestaande gegevens. |
| `apikey.bin`, `forgekey.bin` | De twee keys, versleuteld met Windows DPAPI (alleen jouw Windows-account op deze pc kan ze lezen). **Nooit** in de database, in instellingen of in logs. |
| `logs\` | Logbestanden, zie hieronder. |
| `backups\` | Automatische back-ups vlak voor een import (de laatste vijf). |
| `recipes\` | Je eigen receptbestanden (optioneel). |

Zonder internet blijven je eerder opgehaalde gegevens en je afvinkingen beschikbaar.

### Export en import

Onder "Gegevens en back-up" exporteer je je Legendaries, volgorde, afvinkingen, notities en instellingen naar een
bestand, en importeer je ze weer, bijvoorbeeld op een andere pc. **API-keys zitten nooit in een export.**

- Importeren vervangt je huidige Legendaries en instellingen. De app controleert het bestand eerst volledig en vraagt
  bevestiging; een ongeldig bestand verandert niets en kost ook geen back-up.
- Vlak voor een import bewaart de app wat je nu hebt in `backups\`, dus een vergissing is terug te draaien.
- Kleine fouten in een verder geldig bestand worden overgeslagen en gemeld; een bestand dat geen geldige export is of
  van een nieuwere versie komt, wordt als geheel geweigerd.

### Problemen en logbestanden

De app schrijft een logbestand in `logs\` (één bestand per dag, een week bewaard, hooguit 1 MB per dag). Open de map met
de knop **Logmap openen** in het hoofdvenster.

**Veilig delen.** Voordat iets in het logbestand komt, worden Guild Wars 2-keys, gw2treasures-keys, bearer-tokens,
key-achtige waarden in webadressen en je Windows-gebruikersnaam in paden verwijderd. De app logt geen accountnaam,
inventaris of notities. Lees het bestand toch even door voordat je het deelt. Een regel ziet er zo uit:

```
2026-10-22T19:30:00.0000000+00:00 [WRN] Synchronisatie mislukt: Geen verbinding met de Guild Wars 2-servers.
```

Onverwachte fouten worden gelogd en gemeld; je gegevens zijn veilig, want alles wordt direct opgeslagen. Blijven er
binnen een minuut meerdere fouten komen, dan sluit de app zichzelf af.

## Ontwikkelen

Vereisten: de **.NET SDK 10** (de exacte ondergrens staat in `global.json`) en Git. Bouwen kan ook op macOS en Linux
(`EnableWindowsTargeting`); starten kan alleen op Windows.

```powershell
git clone https://github.com/boderoode/gw2-legendary-tracker.git
cd gw2-legendary-tracker
dotnet build
dotnet test
dotnet run --project src/Gw2LegendaryTracker.App
```

In **VS Code** staan taken (bouwen, testen, publiceren) en een startconfiguratie klaar in `.vscode`; installeer de
voorgestelde extensies (C# Dev Kit). Het formatteren en de analyzers staan strikt: waarschuwingen zijn fouten
(`Directory.Build.props`, `.editorconfig`).

```
src/Gw2LegendaryTracker.Core            Domein: instellingen, voorraad, crafting-engine, doelen, export. Geen UI, geen I/O.
src/Gw2LegendaryTracker.Infrastructure  SQLite, API-clients, DPAPI, logbestand
src/Gw2LegendaryTracker.App             WPF: hoofdvenster, overlay, sneltoets
tests/Gw2LegendaryTracker.Tests         xUnit (de DPAPI-tests draaien alleen op Windows)
tools/Gw2LegendaryTracker.PackBuilder   Hulpprogramma voor ontwikkelaars: receptbestanden genereren en controleren
installer/                              Inno Setup-script
scripts/build.ps1                       Bouwt, test en verpakt zoals de release-workflow
```

### Een build maken

```powershell
./scripts/build.ps1               # herstellen (vergrendeld), bouwen, testen, publiceren, zippen, checksums
./scripts/build.ps1 -Installer    # ...en de installer (vereist Inno Setup 6)
```

Dit is wat er in `artifacts\` komt, en wat de CI ook doet. Handmatig: 

```powershell
dotnet publish src/Gw2LegendaryTracker.App -c Release -r win-x64 --self-contained -o artifacts/win-x64
```

**Reproduceerbaarheid.** Dezelfde invoer geeft dezelfde bestanden: de pakketversies en hashes staan vast in
`packages.lock.json` (de CI herstelt in vergrendelde modus en faalt als er iets afwijkt), de SDK-versie in
`global.json`, release-builds vervangen bronpaden door een vaste waarde, en `.gitattributes` zorgt voor dezelfde
regeleinden in elke checkout. Gecontroleerd:

- dezelfde broncode, gebouwd vanuit twee verschillende mappen: alle 412 bestanden van de publish hebben dezelfde SHA256;
- twee afzonderlijke Windows-runs van de CI op dezelfde commit: ook alle 412 bestanden gelijk.

Niet beloofd: het **zipbestand en de installer zelf** (die bevatten tijdstempels; hun inhoud is wel gelijk), en
bit-voor-bit gelijke bestanden tussen **verschillende besturingssystemen** of SDK-versies. Een build op macOS en een
op Windows verschillen onder andere in padscheidingstekens in de assemblies, in bestandsversies in `deps.json` en in de
opmaak van `runtimeconfig.json`. De bestanden uit de release zijn dus die van de Windows-CI.

### Een release maken

1. Zet de versie in `src/Gw2LegendaryTracker.App/Gw2LegendaryTracker.App.csproj` (`<Version>`) en commit.
2. Repeteer de hele release zonder iets te publiceren: *Actions → Release → Run workflow*. Dit bouwt ook de installer.
3. Maak een tag met dezelfde versie en push hem: `git tag v1.0.0` en `git push origin v1.0.0`. De workflow controleert
   dat tag en projectversie gelijk zijn, bouwt en test alles, en maakt een release met installer, zip en
   `SHA256SUMS.txt`.

### Receptdata genereren (alleen ontwikkelaars)

`data/recipes/legendary.json` is een terugval en testgeval. Opnieuw genereren en controleren:

```powershell
$env:GW2TREASURES_KEY = "<key van api.gw2treasures.com>"
dotnet run --project tools/Gw2LegendaryTracker.PackBuilder -- --root 30704   # 30704 = Twilight; herhaal --root voor meer
```

Het hulpprogramma schrijft niets als een id of naam niet klopt met de officiële API of een recept de officiële
beschrijving tegenspreekt.

## Status en bekende beperkingen

Alle tien fasen van het ontwerp zijn gebouwd. De eigenaar gebruikt de app op Windows; het systematische overlay-testplan
is nog niet afgevinkt.

- Alleen Nederlandstalig; alleen Windows 10 (1809) of nieuwer, x64.
- Programma en installer zijn niet digitaal ondertekend, dus Windows toont een waarschuwing.
- Geen eigen programma-icoon.
- Exclusive fullscreen wordt niet ondersteund (zie de overlay).
- De Legendary-recepten zijn afhankelijk van gw2treasures (zie Recepten).
- Eigen, vaste reserveringen bestaan niet; de volgorde van je Legendaries regelt de verdeling.
