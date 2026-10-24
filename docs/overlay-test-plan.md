# Overlay testen op Windows

De overlay is ontwikkeld op macOS en daar alleen gecompileerd, niet uitgevoerd. Dit plan beschrijft wat op Windows
getest moet worden voordat we iets over de overlay beweren. Vul de kolom *Resultaat* in (OK / niet OK + wat je zag).

**Start:** download de build uit de laatste CI-run (artifact `Gw2LegendaryTracker-win-x64`), pak uit en start
`Gw2LegendaryTracker.exe`. Kies in de app een Legendary (bijvoorbeeld Twilight) zodat de overlay iets toont.

## 1. Schermmodi van Guild Wars 2

Instellingen in het spel: *Options → Graphics Options → Resolution*.

| # | Modus | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 1.1 | Windowed | Overlay zichtbaar boven het spel | |
| 1.2 | Windowed Fullscreen (borderless) | Overlay zichtbaar boven het spel | |
| 1.3 | Fullscreen (exclusive) | **Niet ondersteund**: overlay waarschijnlijk onzichtbaar. Noteer wat er gebeurt. | |

Ondersteuning voor exclusive fullscreen wordt pas geclaimd als 1.3 aantoonbaar werkt.

## 2. Focus en bediening (belangrijkste punten)

| # | Test | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 2.1 | Klik op een knop in de overlay terwijl je in het spel speelt | Het spel houdt de focus (je kunt direct doorlopen of typen) | |
| 2.2 | Open de chat in het spel, klik daarna op ⟳ in de overlay | Chat blijft actief | |
| 2.3 | Sneltoets Ctrl+Alt+L terwijl het spel de focus heeft | Overlay verschijnt/verdwijnt | |
| 2.4 | Zet een andere app op de sneltoets (of start een tweede programma met dezelfde toets) en start de app | Het hoofdvenster meldt dat de sneltoets niet beschikbaar is | |
| 2.5 | Overlay staat boven andere vensters (browser, Verkenner) | Blijft bovenop | |
| 2.6 | Overlay staat niet in de taakbalk of Alt+Tab | Klopt | |
| 2.7 | Sleep de overlay aan de kop | Verplaatst; positie blijft na herstart | |
| 2.8 | Vergrendeling aan, probeer te slepen | Verplaatst niet | |
| 2.9 | Transparantie-schuifregelaar | Overlay wordt doorzichtiger; blijft na herstart | |
| 2.10 | Click-through aan | Klikken gaan door naar het spel; sneltoets en hoofdvenster blijven werken | |
| 2.11 | Click-through weer uit via het hoofdvenster | Overlay is weer klikbaar | |
| 2.12 | Auto-verbergen na 10 seconden aan | Overlay verdwijnt; sneltoets toont hem weer | |
| 2.13 | Verberg de overlay (✕) | Hoofdvenster blijft beschikbaar | |

## 3. Compact en uitgebreid

| # | Test | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 3.1 | Compacte weergave | Icoon en naam, één percentage met de telling ernaast en een dunne balk, "Volgende stap", "Nog nodig" met een mini-balk per materiaal ("120 / 250"), en rechtsonder "Bijgewerkt 12:41" | |
| 3.2 | Aantal materialen op 3 en op 12 zetten (hoofdvenster) | Overlay toont precies dat aantal in "Nog nodig" | |
| 3.3 | Niets meer nodig (alles gedekt) | "Je hebt alles wat nodig is." in plaats van een lege lijst | |
| 3.4 | Geen Legendary gekozen | Vriendelijke tekst en de knop "Hoofdvenster openen" | |
| 3.5 | Uitklappen (pijltje in de kop) | Venster wordt breder en hoger; drie tabs: Nodig, Onderdelen, Boom; de lijst "Nog nodig" verdwijnt omdat de tab "Nodig" alles toont | |
| 3.6 | Tab "Nodig": "Alleen tekort" uit en aan | Lijst toont alle of alleen ontbrekende materialen; blijft na herstart | |
| 3.7 | Tab "Onderdelen": een onderdeel aanvinken (klik ook eens op de tekst, niet alleen op het vakje) | Afgerond vakje dat goud wordt met een vinkje; een klik op de hele regel schakelt het; het hoofdvenster toont dezelfde afvinking zonder herstart | |
| 3.8 | Tab "Boom": een onderdeel uitklappen en een inventaris-sync doen | Uitklapstand blijft behouden | |
| 3.9 | Twee Legendaries met overlappende materialen, onderste selecteren | Onder tab "Nodig" staat "Al in gebruik door hogere Legendaries" | |
| 3.10 | Valuta in het plan (bijvoorbeeld Karma via een eigen verkoper-aanbod) | Onder tab "Nodig" staat het onderdeel Valuta met een balk | |
| 3.11 | Inklappen | Venster wordt weer compact | |
| 3.12 | Knoppen in de kop (bijwerken, meer/minder, verbergen) | Vlak, lichten alleen op bij hover; tooltips kloppen; iconen zijn geen lege vierkantjes (anders ontbreekt het lettertype Segoe Fluent Icons of Segoe MDL2 Assets) | |
| 3.13 | Voortgang op 0%, 100% en tussenin; materiaal volledig gedekt | Balk leeg, vol en deels; een volledig gedekt materiaal heeft een groene balk, een tekort een gouden | |
| 3.14 | Scrollbalk in de overlay (maak het venster laag, of klap uit met veel materialen) en in het hoofdvenster | Dun, afgerond, geen pijlknoppen; iets breder bij hover, goud tijdens het slepen; slepen en het wiel werken | |

## 4. Resoluties, DPI en monitoren

| # | Test | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 4.1 | Windows-schaling 100%, 125%, 150%, 200% | Tekst scherp, venster niet afgesneden | |
| 4.2 | Lage resolutie (1280x720) | Overlay past op het scherm | |
| 4.3 | Twee monitoren: overlay op de tweede monitor, herstart | Komt terug op de tweede monitor | |
| 4.4 | Overlay verslepen tussen monitoren met verschillende schaling | Geen vervorming of verspringen | |
| 4.5 | Tweede monitor loskoppelen terwijl de overlay daar stond, app starten | Overlay verschijnt op een zichtbare plek | |
| 4.6 | Het spel minimaliseren en herstellen | Overlay blijft zichtbaar en bovenop | |
| 4.7 | Uitklappen vlak boven de onderrand of vlak naast de rechterrand van het scherm | Overlay schuift omhoog of naar links en blijft zichtbaar | |

## 5. Gegevens en storingen

| # | Test | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 5.1 | Start de app zonder internet | Opgeslagen gegevens zichtbaar; "laatst gesynchroniseerd" blijft staan | |
| 5.2 | Synchroniseer zonder internet | Duidelijke melding; oude gegevens blijven | |
| 5.3 | Kies een Legendary zonder internet | Opgeslagen recepten werken; melding bij het ophalen | |
| 5.4 | App sluiten en opnieuw starten | Instellingen, doelen, afvinkingen en selectie zijn er nog | |
| 5.5 | Tweede exemplaar starten | Melding dat de app al draait | |

## 6. Uiterlijk van het hoofdvenster

| # | Test | Verwacht | Resultaat |
| --- | --- | --- | --- |
| 6.1 | Knoppen: zweef erboven, klik, en maak er een onbeschikbaar (bijvoorbeeld "Verbinden" tijdens het controleren van een key) | Afgerond en vlak; iets lichter bij hover, donkerder bij klikken, gedimd als onbeschikbaar | |
| 6.2 | Hoofdknoppen ("Verbinden" bij beide keys, "Opslaan" in het formulier) | Goud met donkere tekst; lichter bij hover, donkerder bij klikken | |
| 6.3 | Invoervelden (zoekvelden, prijs, verkoper) en de velden voor de keys | Afgerond; rand wordt goud zodra je typt; tekst en cursor goed leesbaar; geselecteerde tekst heeft een zachte gouden markering | |
| 6.4 | Het notitieveld bij een Legendary met veel tekst | Meerdere regels; er verschijnt een dunne scrollbalk als de tekst langer wordt | |
| 6.5 | Keuzelijst "Betaalmiddel" in het formulier | Afgerond met een pijltje rechts; de lijst klapt open als afgeronde kaart; hover en de gekozen waarde hebben een zachte markering; toetsenbord (pijltjes, Enter) werkt | |
| 6.6 | Sliders (transparantie, aantal materialen) | Dunne baan, goud gevuld tot een ronde gouden knop; knop wordt lichter bij hover en donkerder bij slepen; slepen, klikken op de baan en de pijltjestoetsen werken | |
| 6.7 | Lijst "Mijn Legendaries": selecteer er een | Zachte gouden markering in plaats van Windows-blauw; hover geeft een lichte achtergrond | |
| 6.8 | Alles op 125% en 150% schaling | Niets afgekapt; de ronde hoeken en de balken blijven scherp | |

## Resultaten doorgeven

Noteer per rij OK of niet OK, met wat je zag. Voeg bij problemen een screenshot toe en vermeld Windows-versie,
schermresolutie, schaling en de schermmodus van het spel. Er staan geen API-keys in screenshots van de app (het
sleutelveld is een wachtwoordveld), maar controleer dat toch voordat je iets deelt.
