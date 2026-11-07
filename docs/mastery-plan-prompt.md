<!--
This is the prompt that the "AI-prompt kopiëren" button in the Masteries tab puts on the clipboard.
The app embeds this exact file (see Gw2LegendaryTracker.Core.csproj), removes this comment and replaces
{{VOORTGANG}} with the player's current mastery data from the Guild Wars 2 API. It never adds the API key.
The prompt is in Dutch on purpose: the plan's texts are shown as-is in the app, which is Dutch.
When you change the format below, keep it identical to MasteryPlanSerializer (format "gw2-mastery-plan", version 1).
-->
# Opdracht: maak een masteryplan voor Guild Wars 2

Je bent een ervaren Guild Wars 2-speler en coach. Maak voor mij een concreet stappenplan om mijn masteries verder te trainen. Mijn app (GW2 Legendary Tracker) leest je antwoord in, toont het als stappenplan en vinkt stappen vanzelf af zodra de officiële API laat zien dat ze klaar zijn.

## Harde regels

1. Antwoord met **uitsluitend één JSON-object** in het formaat hieronder: geen uitleg, inleiding of afsluiting. Een ```json-codeblok eromheen mag.
2. Gebruik **alleen echte id's** uit de officiële Guild Wars 2 API: `masteryId` uit `/v2/masteries` en `achievementId` uit `/v2/achievements`. Twijfel je, vul dan `null` in. Verzin nooit een id.
3. `masteryLevel` is **0-gebaseerd**: 0 is het eerste level van een track, net als `level` in `/v2/account/masteries`. Een stap met `"type": "mastery-level"`, een `masteryId` en een `masteryLevel` is klaar zodra dat level getraind is.
4. `region` is exact de `region`-waarde van de track uit `/v2/masteries` (zie de lijst met tracks onder "Mijn huidige voortgang"), of `null`.
5. `waypoint` is een chatcode zoals `"[&BDgAAAA=]"`, of `null` als je hem niet zeker weet.
6. Links mogen alleen naar `https://wiki.guildwars2.com/...`; andere links worden genegeerd.
7. Schrijf alle teksten (titels, doelen, beschrijvingen, notities, aannames) in het **Nederlands**. Namen uit het spel mogen Engels blijven.
8. Elke fase-id en elke stap-id is uniek en hooguit 64 tekens. `requires` verwijst alleen naar stap-id's uit hetzelfde plan, en stappen mogen niet in een cirkel op elkaar wachten.
9. Limieten: hooguit 20 fases en 300 stappen; titels hooguit 200 tekens; beschrijvingen, doelen en notities hooguit 2000 tekens; hooguit 10 links per stap.
10. Sla over wat ik al heb (zie mijn voortgang). Geef realistische `estimatedMinutes`, of `null` als je het niet kunt inschatten.
11. Vul bij twijfel liever `null` in dan een gok. Zet aannames die je doet in `assumptions`.

## Formaat

```json
{
  "format": "gw2-mastery-plan",
  "version": 1,
  "title": "Korte titel van het plan",
  "createdUtc": "2026-01-01T12:00:00Z",
  "author": "Naam van de AI",
  "summary": "Waar het plan op mikt, in een paar zinnen.",
  "assumptions": ["Bijvoorbeeld: je hebt alle uitbreidingen."],
  "phases": [
    {
      "id": "fase-1",
      "title": "Titel van de fase",
      "goal": "Wat je na deze fase hebt bereikt.",
      "steps": [
        {
          "id": "stap-1",
          "title": "Wat je doet",
          "description": "Hoe je het doet, concreet.",
          "type": "mastery-level",
          "region": "Jade",
          "masteryId": 30,
          "masteryLevel": 0,
          "achievementId": null,
          "map": "Seitung Province",
          "waypoint": null,
          "estimatedMinutes": 30,
          "priority": "high",
          "requires": [],
          "links": [{ "label": "Wiki", "url": "https://wiki.guildwars2.com/wiki/Mastery" }],
          "notes": null
        }
      ]
    }
  ]
}
```

Toegestane waarden:

- `type`: `"mastery-point"`, `"mastery-level"`, `"experience"`, `"achievement"`, `"story"`, `"currency"` of `"other"`.
- `priority`: `"high"`, `"normal"` of `"low"`. De app kiest als volgende stap de eerste beschikbare stap met de hoogste prioriteit.
- `createdUtc`: het huidige tijdstip in ISO-8601 (UTC).

## Mijn huidige voortgang

{{VOORTGANG}}

## Mijn wensen

Gebruik de voortgang hierboven. Heb ik hieronder wensen genoteerd (bijvoorbeeld welke uitbreidingen ik heb, hoeveel tijd ik per week heb of waar ik me op wil richten), houd daar dan rekening mee.
