# 3D-karakterviewer en cosmetica-preview: haalbaarheid (fase 1)

Status: onderzoek, er is **geen viewer-code** gebouwd. De Legendary-tracker is niet gewijzigd.
Doel van de gebruiker: alle cosmetische items kunnen bekijken op het eigen karakter.

## Samenvatting

| Onderdeel | Status |
|---|---|
| Catalogus en metadata (skins, dyes, outfits, gliders, minis, eigen uitrusting) | Beschikbaar via de officiële API, gemeten |
| 3D-modellen en textures | Alleen in het lokale `Gw2.dat`, niet via de API |
| Dat-bestand lezen en modellen tonen | Door de gebruiker handmatig bevestigd met een community-browser (zie "Wat is geverifieerd") |
| Koppeling skin-ID naar modelbestand | **Open**, niet gedocumenteerd, moet zelf worden uitgezocht |
| Haar, gezicht, lichaamsopties, animaties | Niet in de API, alleen in `Gw2.dat` (character-compositie), niet uitgezocht |
| "Alle items gerenderd" | Niet te beloven, dekking moet per categorie worden gemeld |

## Wat de officiële API levert (gemeten)

- 3.661 armor-skins (Helm 1.032, Schouders 534, Jas 528, Handschoenen 577, Broek 472, Laarzen 482, Onderwaterhelm 36), 6.469 wapenskins, 443 rugitems, 107 gathering-skins.
- Daarnaast 136 outfits, 148 gliders, 489 mount-skins, 984 minis en 643 dye-kleuren.
- Armor-skins hebben slot, gewichtsklasse (Light, Medium, Heavy, Clothing) en dye-slots. 3.636 van de 3.661 hebben standaard dye-slots (99%). 279 hebben een rasbeperking.
- Dye-kleuren hebben basis-RGB en per materiaal (bijvoorbeeld `cloth`) correcties voor helderheid, contrast, tint, verzadiging en lichtheid met een eindkleur. Dye-voorvertoning is dus te berekenen.
- Eigen gegevens: `/v2/characters/:id/equipment` (huidige skins en dyes), `/v2/account/skins`, `/v2/account/dyes`, `/v2/account/outfits`, `/v2/account/gliders`, `/v2/account/mounts/skins`.
- De enige appearance-gerelateerde route is `/v2/account/dyes`. Er is geen route voor haar, gezicht of lichaam.
- Er zijn **geen** mesh-, model- of texture-verwijzingen, alleen 2D-iconen.

## Waar de 3D-assets zitten

In het lokale `Gw2.dat` van de game-installatie. Referenties:

- [Gw2Browser (kytulendu)](https://github.com/kytulendu/gw2browser), C++, GPL-3.0: modellen bekijken en als OBJ exporteren. Bekende beperkingen: onvolledige texture-verwijzingen, vooral bij uitrusting en character-onderdelen, en tweede UV-kanaal.
- [gw2rustbrowser](https://github.com/webfussel/gw2rustbrowser), Rust, GPL-3.0, laag onderhoud: dat lezen (818.082 bestanden zonder fouten volgens de auteur), MODL v65-v70, textures (DXT1-5, BC7), en het character-compositiebestand (`cmpc`) met 29 race/geslacht-combinaties, skeletten, gezichten, haar en per onderdeel mesh, texture, normal map en dye-masks. Alleen diffuse textures, materialen (AMAT) worden niet nagebouwd, skeletten alleen samengevat.
- [gw2formats](https://github.com/rhoot/gw2formats): leest bestanden type-veilig, maar helpt niet bij het bepalen wat een bestand voorstelt.
- [BlishHUD-assetgids](https://blishhud.com/docs/modules/guides/gw2assets/): bevestigt dat de modellen in `Gw2.dat` zitten. De render-service levert alleen 2D-textures die de API noemt.

## Wat is geverifieerd en wat niet

- Gemeten door mij: de API-cijfers hierboven.
- Door mij gelezen: de broncode-indeling van gw2rustbrowser (`cmpc`-formaat in `crates/gw2dat/src/formats/composite.rs`).
- Gemeld door de gebruiker, niet door mij gezien: bij een handmatige controle met een community-browser op de eigen Windows-pc lijken alle modellen goed te werken. Welke browser, welke stukken en of textures meekwamen is niet vastgelegd. Dit is dus een indicatie, geen testresultaat.
- Niet getest: elk eigen leescode, converteren naar glTF, texturekoppeling, dye-masks, rassen en geslachten.

## Het open probleem: de brug tussen drie soorten ID's

Item-ID's, skin-ID's en `.dat`-bestands-ID's zijn drie aparte ruimtes. De API geeft item en skin, maar niet het interne bestands-ID van mesh of texture. Er is geen formule `meshFileId = skinId`. Een volledig geverifieerde community-index bestaat niet, alleen een handmatig bijgehouden [Dat Index](https://wiki.guildwars2.com/wiki/User:Darqam/Dat_Index) op de wiki.

Aanpak om dit te dichten, in volgorde:

1. Begin klein: één helm, één jas, één paar laarzen, voor één ras en geslacht.
2. Zoek in het dat-bestand hoe de appearance-gegevens van een skin naar modelbestanden verwijzen (waarschijnlijk via `cmpc` en bijbehorende onderdeelbestanden). Dit is reverse-engineering en moet met het echte bestand worden bevestigd.
3. Bewaar het resultaat in een eigen manifest met een expliciete status per skin (`unverified` of `verified`). De viewer toont **alleen** geverifieerde koppelingen, zodat nooit stilzwijgend het verkeerde model verschijnt.
4. Meld de dekking per categorie (bijvoorbeeld "412 van 1.032 helmen geverifieerd"). Nooit "alles werkt" claimen zonder dat het zo is.

## Architectuur (voorstel, nog niet goedgekeurd om te bouwen)

- **Alles lokaal.** De app leest uit het eigen `Gw2.dat` en bewaart afgeleide bestanden alleen onder `%LocalAppData%\Gw2LegendaryTracker`. Geen assets in de repo, de installer of op een server.
- **Pijplijn:** API (skins, dyes, uitrusting) naar manifest (skin naar bestands-ID's) naar lokale exporter (mesh en texture naar GLB) naar viewer.
- **Rendering:** WebView2 met Three.js (keuze van de gebruiker), geladen vanuit een lokale map. Alternatief was native WPF 3D.
- **Exporter:** een eigen leeslaag voor het dat-bestand. De GPL-referenties zijn hulpmiddelen om formaten te begrijpen.
- **Dye:** dye-kleuren uit `/v2/colors` toepassen op de dye-masks, per materiaal.
- **Fasering volgens het oorspronkelijke verzoek:** (1) haalbaarheid, (2) lokale asset-pijplijn voor een kleine testset, (3) viewer met armor, (4) wapens, rugitems en karakteropties, (5) polish en presets.
- **Fallback bij tegenvallers:** een 2D-Fashion-Studio met volledige cosmetica-browser, iconen, berekende dye-voorvertoning, presets en de eigen look uit de API.

## Juridisch en risico's

- De primaire teksten van ArenaNet (Content Terms of Use, User Agreement) waren niet leesbaar voor mij (JavaScript-pagina's). **De gebruiker moet die zelf lezen.** Dit document is geen juridisch advies.
- Volgens secundaire bronnen moeten fanprojecten niet-commercieel zijn, op verzoek van ArenaNet verwijderd kunnen worden, en wordt het uitpakken en herdistribueren van assets niet getolereerd.
- Uitgangspunt daarom: alleen lokaal lezen voor eigen gebruik, geen herdistributie, geen online asset-service.
- Gw2Browser en gw2rustbrowser zijn GPL-3.0. Code daaruit overnemen of porten maakt de app GPL-gebonden. Zolang de app niet wordt gedeeld is dat minder relevant, bij delen wel. Gebruik ze als naslag en eigen gereedschap.
- Het dat-formaat is reverse-engineered en kan met een game-update veranderen. Parser-fouten moeten het oordeel "niet ondersteund" opleveren, nooit een crash of een verkeerd model.
- Prestaties: modellen en textures worden lui geladen en gecachet, de UI blijft bruikbaar tijdens het laden.

## Voorstel van de gebruiker (stappen)

1. Skin-ID's en charactergegevens ophalen via de API.
2. Uitzoeken hoe modellen en textures uit de game-assets komen.
3. Converteren naar glTF/GLB.
4. Renderen met Three.js.
5. Eventueel een eigen API die modellen of afbeeldingen serveert.

Beoordeling: de stappen 1 tot en met 4 passen bij dit plan. Stap 5 wordt afgeraden omdat het herdistributie van assets is. Stap 2 is het open probleem hierboven.
Aanvullende aanpak uit de gebruikersgids: een bouwscript dat uit `/v2/items` de armor-items met `default_skin` haalt. In deze app is dat niet nodig, `/v2/skins` en `/v2/characters/:id/equipment` geven de skin-ID's rechtstreeks. Houd item-ID's, skin-ID's en bestands-ID's gescheiden en laat een ontbrekende `default_skin` `null`.

## Aangepast plan (mijn voorstel op basis van het plan van de gebruiker)

Verschillen met het oorspronkelijke plan, en waarom:

1. **2D eerst, 3D als uitbreiding.** Het grootste risico (skin naar model) kan mislukken of lang duren. De Fashion Studio met catalogus, dye-voorvertoning, presets en de eigen look gebruikt alleen de API en levert altijd waarde. De 3D-weergave wordt een verbetering per categorie, niet een voorwaarde.
2. **Eén go/no-go-poort per fase** in plaats van één groot plan. Na elke poort beslist de gebruiker, op basis van gemeten resultaat.
3. **Geen losse CLI of eigen server.** De dat-reader en GLB-exporter komen als bibliotheek in de app (Infrastructure, in C#). WebView2 leest de lokale cache via een virtuele hostnaam naar een map, zonder webserver en zonder netwerk.
4. **Koppeling zoveel mogelijk automatisch controleren** in plaats van alles met de hand: de API-skins hebben een icoon-URL met het bestands-ID van het 2D-icoon in het dat-bestand. Dat is een mogelijk ankerpunt om skin-ID's aan bestanden te koppelen. Dit is een hypothese die in de spike moet worden bevestigd. Daarna rendert de tool per skin een miniatuur naast het API-icoon, zodat de gebruiker snel kan bevestigen of afkeuren. Alleen bevestigde koppelingen krijgen status `verified`.
5. **Het manifest is lokaal** (`%LocalAppData%`), niet in de repo, omdat het naar ArenaNet-bestanden verwijst en per spelversie kan veranderen. De app bewaart ook de spelversie of het bestandsformaat waarmee het is gemaakt en markeert het manifest als verouderd bij een afwijking.
6. **Eerst statische weergave.** Eerste versie: vaste pose, geen animaties, alleen diffuse texture. Skelet-koppeling van het armor aan het lichaam is een apart risico dat in fase B wordt gemeten.
7. **Dekking per categorie in de UI.** Elk item zonder geverifieerd model toont het icoon met de tekst "geen 3D-voorbeeld beschikbaar". Nooit een verkeerd model.

### Fasen en poorten

| Fase | Inhoud | Poort om door te gaan |
|---|---|---|
| A. Spike (Windows-pc van de gebruiker) | Aparte tool onder `tools/`: dat openen, één icoon-bestand en één model plus texture lezen, de icoon-ankerhypothese testen voor een helm, jas en laarzen | Minstens drie stukken correct herkend en getoond, of een duidelijke reden waarom niet |
| B. Eén armor-stuk op een lichaam | Basislichaam van één ras en geslacht, skelet-koppeling, GLB-export, WebView2 met Three.js | Armor zit op het lichaam zonder zichtbare fouten |
| C. Fashion Studio 2D | Browser links, voorbeeld midden, editor rechts, dye-voorvertoning, lokale presets, eigen look via de API. Kan parallel aan A en B | Werkt volledig zonder `Gw2.dat` |
| D. Dekking uitbreiden | Alle armor-slots, rassen, geslachten, dye-masks, daarna wapens en rugitems | Dekkingscijfers per categorie, steekproef door de gebruiker |
| E. Afwerking | Prestaties, foutafhandeling, haar en gezicht, outfits, gliders, documentatie | Acceptatiecriteria uit het oorspronkelijke verzoek |

### Wat ik daarvoor van de gebruiker nodig heb

- Per poort een korte test op de eigen pc, omdat ik hier geen `Gw2.dat` heb.
- Bevestiging dat de ArenaNet-voorwaarden zijn gelezen (zie het juridische deel hierboven).

## Voortgang

- **Fase C (2D Fashion Studio), eerste versie gebouwd.** Catalogus van alle skins en dye-kleuren (lokaal gecachet, dagelijks tot wekelijks ververst), zoeken en filteren per onderdeel, karakterinstellingen, dye-kanalen, presets en het laden van de look van een karakter. De logica is getest zonder Windows (view model en kernlaag). Het venster zelf is op deze Mac gecompileerd maar nog niet visueel bekeken op Windows.
- Gemeten op de live API met de eigen leescode: 10.539 skins bruikbaar (3.577 armor, 6.418 wapens, 441 rugstukken, 103 gathering) en 643 kleuren. De 141 overige skins hebben geen naam in de API en worden overgeslagen.
- Open: een controle van de gebruiker op Windows (uiterlijk, bediening, laadtijd van de eerste synchronisatie van ruim 50 verzoeken).
- Fase A (spike met `Gw2.dat`) en B (armor op een lichaam) zijn nog niet gestart.
- **Fase A, stap 1 (container lezen) gebouwd.** `DatArchive` leest alleen-lezen de header, de bestandstabel (MFT) en de tabel met bestands-id's van een `Gw2.dat`, ook terwijl het spel draait. Getest met een nagemaakt archief met dezelfde indeling, niet met een echt `Gw2.dat`. Het uitlezen van bestandsinhoud (decompressie) is bewust nog niet gebouwd: eerst moet blijken dat dit deel op een echt archief klopt.
- De tool `tools/Gw2LegendaryTracker.DatSpike` heeft twee opdrachten: `info` (grootte en aantallen van het archief) en `icons` (controleert de hypothese dat het icoon-id in de URL van een API-skin ook een bestands-id in het archief is, per categorie). Beide schrijven niets weg en kopiëren niets uit het archief.
- Wat nog moet gebeuren na een goede uitkomst: decompressie, het lezen van een model en texture, en het uitzoeken van de koppeling van skin naar model.
