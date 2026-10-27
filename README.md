# GW2 Legendary Tracker

A Windows app for tracking your Legendary crafting progress in Guild Wars 2. One program with two views:
a regular **main window** and a compact **overlay** that can stay visible over the game. Both use the same
data and calculations, so a change in one window is immediately visible in the other.

The app only reads data through the official Guild Wars 2 API. Nothing in the game is modified: no DLL injection,
no reading game memory, and no automation.

## Installation

You need Windows 10 (version 1809) or later, 64-bit. You do not need to install anything else; .NET is included.

**Using the installer (recommended)**
1. Download `Gw2LegendaryTracker-Setup-<version>.exe` from [releases](../../releases).
2. Run it. It installs for your user account, without administrator privileges, and creates a Start menu shortcut.
3. You can uninstall it via *Settings → Apps*. The uninstaller asks whether you also want to delete your data.

**Or use the ZIP archive**
1. Download `Gw2LegendaryTracker-<version>-win-x64.zip` from the releases and extract it to a folder of your choice.
2. Run `Gw2LegendaryTracker.exe`. You can move the app by copying the entire folder.

**Windows says the program isn't trusted.** The app is not digitally signed (that requires a paid certificate). Click *More info* and then *Run anyway*. To make sure the file has not been changed, compare its SHA256 value with `SHA256SUMS.txt` from the release (`Get-FileHash <file>` in PowerShell).

## First use

1. **Connect your account.** Create a key at <https://account.arena.net/applications> with only the permissions
   **account**, **inventories**, **characters**, and **wallet**. Paste it into the main window and click *Connect*.
   The app checks the key and rejects it if any permissions are missing.
2. **Add your gw2treasures key** for Legendary recipes themselves (instructions are shown in the app; see also
   [Recipes](#recipes)).
3. **Choose a Legendary** under *My Legendaries*. The order determines which ones get your materials first.
4. On first launch, approximately 13,000 recipes are downloaded; this takes a few minutes, and progress is displayed.
5. Start Guild Wars 2 in **Windowed** or **Windowed Fullscreen** mode and show the overlay with **Ctrl+Alt+L**.

## The overlay

The overlay is designed to be glanced at while playing: in two seconds, you can see how far along you are, what
you should do next, and what you are still missing.

- **Compact** (default): icon and name of the selected Legendary, your progress as a single percentage (with the count
  beside it and a thin bar), the **next step**, and **still needed**: the largest shortages, each shown as "120 / 250" with a
  mini bar (gold while something is missing, green when covered). The bottom-right shows when it was last updated. You
  choose the number of materials to display (1 to 20).
- **Expanded** (arrow in the header): the window becomes wider and taller and shows three tabs instead of mixing
  everything together: **Needed** (all materials, with an *Shortage only* filter, currencies, and what higher-priority
  Legendaries in your list are already using), **Components** (checklist), and **Tree** (expandable crafting tree).
- **No Legendary selected:** a short explanation and a button to open the main window.
- **Controls:** drag by the header, lock it against dragging, adjust transparency, auto-hide, and use a
  *click-through* mode in which mouse clicks pass through the overlay to the game. The hotkey and all settings
  are in the main window, so you can always regain control.
- **Focus:** clicking the overlay does not steal focus from the game.
- **Progress** is the share of components in the crafting tree that are complete: you have enough of them, or you can
  craft them now with what you own. 100% means you can complete the goal right now.
- **Reservations** are not separate quantities: the order of your Legendaries determines who gets materials first. The
  *Needed* tab shows which materials are already being used from your inventory by higher-priority Legendaries.
- The icons in the header come from the *Segoe Fluent Icons* font (Windows 11) or *Segoe MDL2 Assets* (Windows 10).

### Known overlay limitations

- The overlay is a regular Windows window that stays on top. This works over **windowed** and **borderless
  fullscreen** modes. **Exclusive fullscreen is not supported** and has not been tested; choose *Windowed Fullscreen*.
- Behaviour over the game (display modes, multiple monitors, different scaling settings, focus) has not yet been
  measured systematically. The test plan is in [`docs/overlay-test-plan.md`](docs/overlay-test-plan.md).
- The overlay does not read anything from the game client: everything comes from the API, recipes, and items you check off yourself.

## Trading Post costs (main window)

For your selected Legendary, the main window shows how much it would cost to buy what you are still missing, using current
Trading Post prices. The overlay does not show prices and has not been changed for this feature.

- **Total and your wallet:** the cost of buying everything immediately versus using buy orders, and whether you have enough gold or how much you are short.
- **Per material:** quantity, price per item, and total amount, with the most expensive items at the top.
- **Smarter buying:** for components you would craft yourself, such as an ingot made from ore, the app compares the purchase price
  with the cost of the missing raw materials. If buying is cheaper, it shows how much you save. Tips never overlap:
  if a component is bought, the app no longer considers what goes into making it.
- **Not for sale:** items the Trading Post does not sell (account-bound items, rewards, ...) are listed separately and do not
  count toward the total.
- **Freshness:** prices are fetched and cached at most once every 10 minutes. Without internet, you see the last known
  prices with their timestamp and a notice that refreshing failed. *Refresh prices* requests them again immediately.

**This is an estimate.** The app calculates using the lowest sell price. For large quantities, the price increases because the
cheapest listings run out, so the actual total may be higher. Crafting costs and time are not included. The price paid for an
immediate purchase is the lowest listing price (`sells` in the API); a buy order outbids the best order by 1 copper, but never
costs more than buying immediately.

## Vendors and currencies (Karma, ...)

Not everything is bought from the Trading Post. For example, Obsidian Shards can be obtained from a vendor using Karma.
The app accounts for this when you record where the item comes from.

**Why do you have to record this yourself?** There is no source the app can read. The official API has a route for
vendors (`/v2/vendors`), but it is inactive (response: *API not active*); gw2treasures item pages do not list it, and
the wiki was under maintenance when this feature was being built. The app does not make up prices: an incorrect
number results in a shortage you might trust.

- **Recording a source:** under the *Not available on the Trading Post* heading, each item has a **Record source…**
  button. Choose the payment currency (for example, Karma), the price, how many items you receive, and optionally
  the vendor and location. Look up the details using the **Wiki** and **gw2treasures** buttons for that item.
- **What the app does with it:** the item disappears from "not for sale". The planner treats the purchase as a
  step: *"Buy now: 250× Obsidian Shard (from vendor: …)"*, and the Currencies section shows whether you have enough Karma
  (*"you have 194,832 of 6,250"*) or how much more you need. If the item is also on the Trading Post, its price appears
  beside it so you can choose.
- **Saved and included:** your offers are stored in the database and included in an export, and therefore in a backup too.
  You can delete an offer under *Sources and custom data*.
- **Wiki and gw2treasures:** the buttons open the page in your browser. The Wiki button searches for the item name (an
  exact name takes you directly to the page); the gw2treasures button opens that exact item ID. Only links to these two
  sites are opened.
- **Custom data folder:** *Open custom data folder* opens `%LOCALAPPDATA%\Gw2LegendaryTracker\recipes`. A recipe file
  placed there is read at startup. This is useful for lots of data at once or for an offer with multiple costs.
- **Form limitations:** one payment currency per offer, and Coin is not included (amounts are in copper, which leads to
  errors). Both are possible through a file.

## Fashion Studio (in development)

Use *Open Fashion Studio* in the main window to put together a look. Browse all skins that fit the selected slot on the
left, see your look in the middle, and choose race, gender, profession, and dyes on the right.

- **What it does:** all armor, weapon, and back skins from the official API, the more than 600 dye colours, name search,
  race and profession filters, and saved looks (presets) on your own computer.
- **My look:** *Load my characters* retrieves what a character is currently wearing. The API leaves slots empty when
  an item displays its default skin, so those slots remain empty.
- **What I have unlocked:** the *Unlocked* marker and *Only show what I've unlocked* filter require the API permission
  `unlocks`. Everything else works without that permission, but there are no unlock markers. Create a new key with that extra permission to use these features.
- **It is a 2D preview.** The API provides icons and colours, not 3D models. Colour swatches are shown as samples; the
  skin icon itself is not recoloured. Research into 3D is documented in `docs/character-viewer-feasibility.md`.
- Skins without a name in the API (approximately 140, mostly unused) are not shown.
- Saved looks are not yet included in *Export* and *Import*, so do not keep your only copy there.

## What the app does and does not count

Inventory consists of your **bank, materials storage, shared inventory, and the bags of all your characters**. It does
not count equipped gear, the Trading Post, the guild bank, or other locations not provided by the inventory endpoints.
If you have crafted or obtained a component that the app cannot see, you can check it off; everything below it will then be excluded.

## Recipes

The recipe book comes from three sources:

1. **The official API:** all approximately 13,200 standard recipes, downloaded once and stored locally.
2. **gw2treasures:** Mystic Forge recipes for Legendaries and their components, which the official API does not include. Of
   the 206 Legendaries, the official API only has recipes for the 18 Obsidian armor pieces.
3. **Local files** in `data\recipes` (included, Twilight) and `%LOCALAPPDATA%\Gw2LegendaryTracker\recipes`
   (your own), for anything not available elsewhere, including currency costs.

**Fetching from gw2treasures.** When you select a Legendary, the app fetches the recipes for that Legendary and all its
components. A free key that you create yourself is required (the steps are shown in the app).

- The endpoint returns one item per request; the app traverses the tree and remembers each component for seven days. Shared
  components (Gift of Fortune, Gift of Mastery, ...) are fetched only once: Twilight took 13 requests, and Bifrost
  took 6 after that. Saved recipes continue to work without internet.
- At least 1.6 seconds between requests and no more than 150 per run. Raw materials are not looked up.
- Each recipe is compared with the official item description ("Made by combining these items in the Mystic Forge").
  If it differs, it is not used and the app reports it. If there is no description, the recipe is saved but flagged as
  "one source, not verified".
- gw2treasures may later make this endpoint available only after approving your application; the app reports this and
  uses what has already been saved.
- The data comes from gw2treasures, a community project. Check their terms before sharing the app or recipe files
  beyond private use.

**Calculation.** The engine uses what you own first and calculates the remainder recursively. It reuses surplus, chooses
the route with the fewest missing items when multiple recipes exist (then the fewest crafting actions, then the lowest
recipe ID), and identifies the next step. Recipes requiring guild upgrades and recipes you must learn first are not counted.

**Custom recipe files** use this format. IDs are negative so they never conflict with API recipes; invalid entries are skipped
and reported. Include the **name** for every item and currency: the app checks IDs and names against the official API so an incorrect ID stands out.

```json
{ "recipes": [
  { "id": -1, "output": { "itemId": 100, "count": 1, "name": "Gift of Might" },
    "ingredients": [ { "itemId": 5, "count": 2, "name": "Some Item" }, { "currencyId": 23, "count": 250, "name": "Spirit Shard" } ] },
  { "id": -2, "vendor": { "name": "Vendor name", "location": "Location" },
    "output": { "itemId": 19925, "count": 1, "name": "Obsidian Shard" },
    "ingredients": [ { "currencyId": 2, "count": 25, "name": "Karma" } ] }
] }
```

A recipe with a `vendor` entry is a vendor offer; without that entry, it is a Mystic Forge recipe.

## Data, privacy, and security

**Network traffic.** The app communicates with `api.guildwars2.com` (your account using your key, plus public lists and Trading Post prices),
`api.gw2treasures.com` (recipes, using your gw2treasures key), and `render.guildwars2.com` (item icons). There is no
telemetry, no analytics, and no other server. The GW2 key is only used for read requests.

**Storage** in `%LOCALAPPDATA%\Gw2LegendaryTracker\`:

| File or folder | Contents |
| --- | --- |
| `tracker.db` | SQLite: settings, goals, checkmarks, notes, your custom source data, last inventory, cached item names, recipes, and Trading Post prices. The schema is versioned; updates migrate existing data. |
| `apikey.bin`, `forgekey.bin` | The two keys, encrypted with Windows DPAPI (only your Windows account on this PC can read them). **Never** stored in the database, settings, or logs. |
| `logs\` | Log files, see below. |
| `backups\` | Automatic backups immediately before an import (the last five). |
| `recipes\` | Your own recipe files (optional). |

Previously retrieved data and your checkmarks remain available without internet.

### Export and import

Under "Data and backup", you can export your Legendaries, their order, checkmarks, notes, source data, and settings to a
file, and import them again, for example on another PC. **API keys are never included in an export.**

- Importing replaces your current Legendaries and settings. The app validates the entire file first and asks for
  confirmation; an invalid file changes nothing and does not create a backup either.
- Immediately before an import, the app saves your current data in `backups\`, so you can undo a mistake.
- Minor errors in an otherwise valid file are skipped and reported; a file that is not a valid export or comes from
  a newer version is rejected entirely.

### Problems and log files

The app writes a log file in `logs\` (one file per day, retained for one week, at most 1 MB per day). Open the folder with
the **Open log folder** button in the main window.

**Safe to share.** Before anything is written to the log, Guild Wars 2 keys, gw2treasures keys, bearer tokens,
key-like values in web addresses, and your Windows username in paths are removed. The app does not log your account name,
inventory, or notes. Still, review the file before sharing it. A line looks like this:

```
2026-10-22T19:30:00.0000000+00:00 [WRN] Synchronization failed: No connection to the Guild Wars 2 servers.
```

Unexpected errors are logged and reported; your data is safe because everything is saved immediately. If multiple errors
continue to occur within a minute, the app shuts itself down.

## Development

Requirements: the **.NET SDK 10** (the exact minimum version is specified in `global.json`) and Git. The project can also
be built on macOS and Linux (`EnableWindowsTargeting`); it can only be run on Windows.

```powershell
git clone https://github.com/boderoode/gw2-legendary-tracker.git
cd gw2-legendary-tracker
dotnet build
dotnet test
dotnet run --project src/Gw2LegendaryTracker.App
```

In **VS Code**, tasks (build, test, publish) and a launch configuration are ready in `.vscode`; install the
recommended extensions (C# Dev Kit). Formatting and analyzers are strict: warnings are errors
(`Directory.Build.props`, `.editorconfig`).

```
src/Gw2LegendaryTracker.Core            Domain: settings, inventory, crafting engine, goals, export. No UI, no I/O.
src/Gw2LegendaryTracker.Infrastructure  SQLite, API clients, DPAPI, log file
src/Gw2LegendaryTracker.App             WPF: main window, overlay, hotkey
tests/Gw2LegendaryTracker.Tests         xUnit (DPAPI tests run only on Windows)
tools/Gw2LegendaryTracker.PackBuilder   Developer utility: generate and validate recipe files
installer/                              Inno Setup script
scripts/build.ps1                       Builds, tests, and packages as in the release workflow
```

### Creating a build

```powershell
./scripts/build.ps1               # restore (locked), build, test, publish, zip, checksums
./scripts/build.ps1 -Installer    # ...and the installer (requires Inno Setup 6)
```

This is what goes into `artifacts\`, and what CI does as well. Manually:

```powershell
dotnet publish src/Gw2LegendaryTracker.App -c Release -r win-x64 --self-contained -o artifacts/win-x64
```

**Reproducibility.** The same inputs produce the same files: package versions and hashes are pinned in
`packages.lock.json` (CI restores in locked mode and fails if anything differs), the SDK version is pinned in
`global.json`, release builds replace source paths with a fixed value, and `.gitattributes` ensures the same line endings
in every checkout. Verified:

- The same source code, built from two different directories: all 412 published files have the same SHA256.
- Two separate Windows CI runs on the same commit: all 412 files are identical as well.

Not guaranteed: the **ZIP archive and installer themselves** (they contain timestamps; their contents are identical), and
bit-for-bit identical files across **different operating systems** or SDK versions. A build on macOS and one on Windows
differ in, among other things, path separators in assemblies, file versions in `deps.json`, and the formatting of
`runtimeconfig.json`. The files in the release are therefore the ones produced by Windows CI.

### Creating a release

1. Set the version in `src/Gw2LegendaryTracker.App/Gw2LegendaryTracker.App.csproj` (`<Version>`), write the notes in
   `docs/release-notes/v<version>.md` (the workflow refuses a release without them), and commit.
2. Rehearse the entire release without publishing anything: *Actions → Release → Run workflow*. This also builds the installer.
3. Create a tag with the same version and push it: `git tag v1.0.0` and `git push origin v1.0.0`. The workflow checks
   that the tag and project version match, builds and tests everything, and creates a release with the installer, ZIP, and
   `SHA256SUMS.txt`.

### Generating recipe data (developers only)

`data/recipes/legendary.json` is a fallback and test case. To regenerate and validate it:

```powershell
$env:GW2TREASURES_KEY = "<key from api.gw2treasures.com>"
dotnet run --project tools/Gw2LegendaryTracker.PackBuilder -- --root 30704   # 30704 = Twilight; repeat --root for more
```

The tool writes nothing if an ID or name does not match the official API or if a recipe contradicts the official
description.

## Status and known limitations

Version 1.0.0. All ten design phases have been implemented. The owner uses the app and overlay on Windows; the
systematic overlay test plan and the installer on a clean computer have not yet been completed.

- Dutch language only; Windows 10 (1809) or later, x64 only. (English WIP)
- The app and installer are not digitally signed, so Windows displays a warning.
- No custom application icon.
- Exclusive fullscreen is not supported (see the overlay section).
- Legendary recipes depend on gw2treasures (see Recipes).
- Vendor data (Karma purchases, etc.) must be entered manually because there is no source for it (see Vendors and currencies).
- The overlay has deliberately not been changed for vendors; in the expanded view, Karma and other currencies appear automatically in the Currencies section when the plan requires them.
- Custom, fixed reservations do not exist; the order of your Legendaries controls the allocation.
