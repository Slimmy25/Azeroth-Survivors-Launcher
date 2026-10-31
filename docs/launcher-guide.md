# Azeroth Survivors Launcher

> **Azeroth Survivors Launcher** is a free Windows companion for the game: install and update it, manage addons and
> cloud saves, read the Guide, track runs, and plan builds. The launcher is community-made; the game remains hosted
> by its endorsed download sources.

It supports **[Azeroth Survivors](https://wow.aethus.pl/)**, the survivors-style game made by **Aethus**.

This is a fan project. It is not made by Aethus, and it is not affiliated with or endorsed by Blizzard Entertainment.
The launcher does not contain or host the game: it downloads it from the game's own download hosts. See
[CREDITS.md](../CREDITS.md) for where everything comes from.

**[Download the launcher](https://github.com/Slimmy25/Azeroth-Survivors-Launcher/releases/latest)**: one file,
`Azeroth Survivors Launcher.exe`. Put it beside your game, or in the folder where you want the game installed.
It runs on .NET Framework 4.8.

<table>
  <tr>
    <td align="center"><img src="screenshots/home-community.png" width="420" alt="The home screen with a featured Community video"><br><sub>Home: featured Community videos and the game's news</sub></td>
    <td align="center"><img src="screenshots/stats-overview.png" width="420" alt="The STATS overview with sample run totals and time in runs"><br><sub>STATS: sample runs, wins, time in runs and enemies defeated</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="screenshots/talent-tree-builder.png" width="420" alt="The Talent Tree Builder with a Warrior build"><br><sub>Theorycrafting: try a talent build on the game's trees</sub></td>
    <td align="center"><img src="screenshots/gear-planner-tier-bonus.png" width="420" alt="The Gear Planner with a Priest set and jewelry equipped"><br><sub>Theorycrafting: compare a class set and jewelry on the character sheet</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="screenshots/guide-class-gear.png" width="420" alt="The Paladin Guide with its in-article contents tree"><br><sub>Guide: a class passage with a live contents tree beside it</sub></td>
    <td align="center"><img src="screenshots/options-path.png" width="420" alt="Options folders with save import and export"><br><sub>Options: folder locations and portable save import/export</sub></td>
  </tr>
</table>

The STATS screenshot uses made-up sample data. The other screenshots show the local launcher with example selections.

---

## What it does

### First-time setup and game installation
- A new launcher location starts **First Time Setup**, even if another copy has been used before. Its marker stays
  beside the executable until setup succeeds; cancellation does not mark setup complete.
- Cancel Setup removes the launcher's own setup data and temporary download files, while preserving the game, saves,
  addons, addon settings and cloud files.
- Players moving from the browser game can import an exported `.sav` during setup. Existing client owners select
  their game folder; new players can install the game beside the launcher.
- Setup asks whether you are new or already have the game. Existing players select the folder containing
  `AzerothSurvivors.exe`; that route skips downloading the game. New players reach the download as the final setup
  step. The game begins downloading when they choose to continue from “Ready to download and play?”.
- Cloud saving is off by default for a new install; saved preferences from an existing install are preserved. When
  enabled, it syncs the game's save directory and addon settings to the
  chosen cloud folder. The game controls where it writes live saves (by default `%APPDATA%\wenilla-rogue`); changing
  the launcher's save-folder field only changes the folder used for import and sync. Addon program files are not synced.
- Downloads use the game's hosted files, try alternate hosts after failures and verify the archive when a trusted
  checksum is available. The Google Drive mirror's existing MD5 verification is a deliberate project policy.
- The launcher checks for its own update at startup and before launching the game. The game also updates itself
  when it starts. During a game or launcher download/install, closing opens an in-launcher choice to cancel and exit or stay.

### Addon manager
- **Your addons**: turn each one on or off, uninstall it (its folder and saved settings), toggle them all, and keep
  two profiles of which ones are on. They are grouped like the store: AZS addons, WoW 1.12 addons, your own and
  anything else.
- **Download More**: install, update or uninstall addons with one click.
  - **AZS addons** are made for Azeroth Survivors, by Slimjim and other players.
  - **WoW 1.12 addons** are classic addons converted to work in this game.
  - **My Addons** lists the addon .zip files you keep on your own PC.
- Addons from Download More are installed only when chosen. First-time setup offers Maidenhead's Azeroth Stats; you can decline it.

### The Guide
The Guide has **New Player** and **Gameplay** sections. **Theorycrafting** is a Gameplay menu:
- **New Player**: getting started, controls, a first run, beginner tips, pickups, caches and guardians, talents, gear
  and the Armory, addons, achievements and rewards, level-ups and cards, and co-op.
- **Gameplay**: heroics and endless mode, all nine class guides, races, zones and bosses, **Items**, **Archive**,
  **Transmog**, and player-made **Build Guides**. Class guides share a linked passage for spells, talent descriptions,
  gear, Codex entries and class-specific resources.
- **Items** contains every class's gear and set bonuses, plus shared rings, trinkets and ordinary amulets. **Archive**
  preserves the flat alphabetical Codex record for reference; Archive is omitted from search, and links lead to the
  relevant entry in the class guide instead.
- **Gameplay > Theorycrafting**: the **Talent Tree Builder** and **Gear Planner**.

Search covers player-facing guide pages, not Archive. Spell, talent, gear and Codex links keep their surrounding text
and lead to the matching passage or class-guide location. The class contents tree stays beside the passage; scrolling
it jumps between sections and the passage follows. Spell and talent hovers show their details; item hovers also show a
Wowhead Classic 3D model preview when an item match is available, including in the Gear Planner.
Short how-to videos play inside the launcher. The same guide is in the game with **AZS Handbook** (`/handbook`).

### Tools to theorycraft
- **Talent Tree Builder**: all three trees of every class, as in the game. Click to add a point and right-click to
  take one back. Each row opens with 5 points per row above it in its own tree, up to 51 points. The build is
  cleared when you leave the trees.
- **Gear Planner**: a character sheet laid out like the game's. Put a class's gear and any rings, trinkets and
  amulet in its slots and see what they add, on the sheet's stats and the Run Stats beside it. Gear whose bonuses
  are not known yet is marked.
- **Build Guides**: player videos are grouped by class when their title or description identifies one; some classes
  may not have a published guide yet.
- **Saved talent builds**: names are shared across classes. Each saved build remembers its class and opens that class's
  Talent Tree Builder when selected.
- **STATS** (optional, with Maidenhead's Azeroth Stats): run totals, classes, history, records, Nemesis evidence,
  observed co-op samples, combat sources, spell-rank trends, build journal and retained equipment snapshots. It shows
  each counter the addon actually saved, including healing and magnets, and leaves unavailable values blank. The
  launcher's reader does not change addon data. Use `/azstats` in game for the addon's full reports and comparisons,
  configurable HUD, favorites, and local-data editing. Existing AZS Stats files are kept read-only and are not merged;
  the replacement records time in runs, not time spent in menus or out of runs.

### Community videos
The home screen features videos made by the community, in rotation. **MORE VIDEOS** lists them all. They stream from
YouTube in a launcher-managed video window and are not saved as video files. Each creator's name links to their channel.

### Also
- **Cloud Save** (Options > Cloud Save): your save and your addons' settings kept in Google Drive, OneDrive or Dropbox and shared between
  your PCs. The addons themselves aren't copied (an old copy could replace a newer one): when settings arrive for an
  addon from Download More that isn't installed, the launcher installs it.
- **Import / Export Save** (Options > Folders): import or export a validated `.sav` file for the browser game or another
  client. Importing over existing progress keeps a backup in the game's Saves folder.
- **News and Changelog**: every patch note of the game, newest first.
- **Options**: the game's own settings (display, graphics, sound, gameplay) with a plain explanation for each, your
  folders, and the launcher's own settings.
- **26 languages** and only one copy running at a time. Closing exits the launcher; it does not stay in the system tray.

### The AZS addons
| Addon | What it does |
| --- | --- |
| **Azeroth Stats** (`/azstats`) | Maidenhead's optional tracker. The launcher reads its local SavedVariables for run totals, history, combat samples and observed build changes. |
| **AZS Handbook** (`/handbook`) | The launcher's whole Guide, inside the game. |
| **AZS Minor Mods** (`/azsmods`) | Small changes, each on or off: no level-up, stat-bonus or new-spell lines in chat, boss markers, a blinking marker on a friend who falls, move and lock the UI, hide the target frame, a plain action bar of just the six spell squares (no gryphons), the camera at its farthest. |

They use the game's addon API and share one look (`addons/AZSKit`).

---

## Where it keeps its files

Everything the launcher writes for itself goes into a folder named `Azeroth Survivor Launcher` next to the exe:
settings (`config.json`), the cloud sync memory (`state.json`), `log.txt`, backups made before a sync replaces
anything (`backups`), the last 5 cloud downloads (`downloads`) and temporary files (`temp`).

It uses `%LOCALAPPDATA%\Azeroth Survivor Launcher` instead when the exe's folder can't be written to (for example
Program Files), or when the exe is inside Google Drive, OneDrive, Dropbox or iCloud.

## What it reaches on the internet

- **The game's download hosts** (in `src/Launcher.cs`): the game client.
- **GitHub**: launcher updates and the addons offered in Download More.
- **jsDelivr** (a CDN for GitHub files): the guide's how-to videos, streamed while they play.
- **YouTube**: community and build videos and their titles; Community videos play in a Microsoft Edge window managed by the launcher.
- **Wowhead's image host** (`wow.zamimg.com`): spell, class, race, zone and creature pictures, plus item-model previews in guide and Gear Planner hovers. Images load
  when needed and are cached in the launcher's `temp` folder.
- **Your cloud drive's folder on this PC** (Cloud Save), and nothing else of your files.

---

## Build it yourself

Double-click `build.bat`. It uses the C# compiler that comes with Windows (C# 5, .NET Framework), so nothing has to
be installed. The exe appears in `dist`. Every push is also built on GitHub (**Actions** tab).

| What | Where |
| --- | --- |
| Patch notes | `assets/changelog.txt` (posts separated by a line with only `===`) |
| Download More list | `assets/addons.txt`, and where each copy comes from: `addons/sources.txt` |
| The Guide | `assets/guide.txt` (format at the top); its groups: `GuideGroups` in `src/Guide.cs` |
| Guide videos | `guide-videos/<topic>/` (published by Actions > Publish guide videos) |
| Community videos | `Headlines` in `src/Community.cs`, then the Build Guides videos |
| Talent trees | `assets/talents.txt` |
| Translations | `assets/lang/<code>.txt` (see `assets/lang/README.md`) |
| The AZS addons | `addons/AZSHandbook`, `addons/AZSMinorMods`, shared look `addons/AZSKit`; Azeroth Stats is maintained separately and downloaded from its author |
| Where launcher updates come from | `Repos` in `src/SelfUpdate.cs` |

Code signing is optional: `sign_build.bat` explains it. Never commit `sign_config.bat` or a certificate
(`.gitignore` keeps them out).

## Download hosts

The launcher only downloads the game from hosts officially endorsed by both Aethus and the Azeroth Survivors mod
team. Unofficial mirrors are never used.

| Host | Run by | Check |
| --- | --- | --- |
| Primary download host (`budapestmix.hu`) | Project endpoint | Pinned SHA-256 and MD5 |
| Developer backup (`azerothsurvivors-mirror.sandervspl.dev`) | Project backup | Host-provided SHA-256 plus pinned checks |
| Google Drive | Fotze, trusted head moderator | Pinned MD5 or matching SHA-256 |
| Official website fallback | Aethus | Accepts a real `.rar`, `.zip` or `.7z` from the game site; checks the pinned SHA-256 when it matches |

The project owner considers the Google Drive mirror from Fotze, a trusted head moderator, a trusted source and has
chosen to keep its existing MD5 check. This is a deliberate project policy. The primary and developer backup hosts
use their pinned checks; the official-site fallback is accepted only as a real game archive from Aethus's site.

## License

The launcher's code and the AZS addons are under the [MIT License](LICENSE). Everything that comes from someone else
keeps its own license and owner: see [CREDITS.md](../CREDITS.md). The game's name and its logo (the "AS" icon the
launcher uses for its shortcut, `assets/AzerothSurvivors.ico` and `assets/portrait.png`) belong to Aethus and are not
covered by the MIT License. The emblem beside the title (`assets/logo.png`, the cat heroes) is Slimjim's own art.
