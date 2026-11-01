# Credits and sources

The Azeroth Survivors Launcher is made by **Slimjim** ([Slimmy25](https://github.com/Slimmy25)). It stands on a lot of
other people's work. This page says where everything comes from, who owns it, and what this repository does and
does not contain.

## Thanks: the people who peer reviewed this work

- **Keijin** has been there since the beginning, and is the main reason I wanted to get into this. His work also helped me learn about more nuanced addon settings and work through some confusing questions along the way. Genuinely, a big thanks to him.
- **Kraatos** spent a little time helping me launch into open source; I'm grateful for his insight on problems still to come.
- **Maidenhead** has been an exceptional advisor, spending extensive time helping me clarify and realize my vision for this project. His insight and support have been invaluable.
- **The Azeroth Survivors Discord Mod team** has supported the project's transparency and made room for addon review. Thank you for the time and feedback.

## The game: Azeroth Survivors, by Aethus

**Azeroth Survivors** is made by **Aethus**. It is played at <https://wow.aethus.pl/> (in the browser) and as a
Windows client, `AzerothSurvivors.exe`. The game, its code, its data, its patch notes and its logo belong to Aethus.

- **The game client is not in this repository and is not hosted by this project.** The launcher uses a primary download
  endpoint and a Google Drive mirror from Fotze, then tries the game's own page at <https://wow.aethus.pl/download> as
  a last resort. The mod team removed a former developer backup mirror on 2026-11-01. Regular-host downloads are
  checked using the project-maintained checks in `src/Launcher.cs`.
  The project owner considers Fotze's Google Drive mirror a trusted source and has deliberately retained its MD5
  verification policy.
- **Game data** the launcher shows (talent trees, spell and talent descriptions, names) is the game's own, by Aethus.
  The Guide was written for the launcher from what the game shows and says.
- **Patch notes** in News and Changelog (`assets/changelog.txt`) are the game's own, by Aethus.
- **The game's addon API** used by the AZS addons is documented by the game.
- **The game's logo** (the gold "AS" on its blue globe: the launcher's shortcut and window icon,
  `assets/AzerothSurvivors.ico`, and the small round picture in the corner of its windows, `assets/portrait.png`) is
  Aethus's work and belongs to Aethus.

## World of Warcraft and Blizzard Entertainment

Azeroth Survivors runs on the World of Warcraft 1.12 interface, and is set in Blizzard's world of Azeroth. World of
Warcraft, Azeroth and every name, picture, model, sound and other asset from it are trademarks and copyrighted work of
**Blizzard Entertainment, Inc.** This project is not affiliated with or endorsed by Blizzard.

Most Blizzard material is loaded from its source when shown. The launcher also bundles selected screenshots and icons;
their sources are listed here:

| What you see | Where it comes from |
| --- | --- |
| The game itself, with its models, art and sounds | The game client, downloaded from the game's hosts (above) |
| Spell, talent, class, race, zone and creature icons and pictures in the launcher | Loaded when shown from **Wowhead**'s public image host, <https://wow.zamimg.com>, and cached only in the launcher's `temp` folder. Which icon belongs to which spell is in `assets/spell-icons.txt` (names only). |
| Item model preview pictures in Guide, Gear Planner and Transmog hovers | Wowhead Classic model-viewer thumbnails from <https://wow.zamimg.com/modelviewer/classic/webthumbs/item/>. The item-to-model data is in `assets/item-models.txt`; exact items and same-set slot synonyms only. |
| Zones & Bosses favorite icon (`assets/favorites/zones-bosses.jpg`) | `Naxxico.jpg`, a Naxxramas icon from [Wowpedia's file page](https://wowpedia.fandom.com/wiki/File:Naxxico.jpg), originally listed in its [small icons index](https://wowpedia.fandom.com/wiki/Wowpedia:List_of_small_icons?file=Naxxico.jpg). Blizzard owns the underlying World of Warcraft artwork. |
| Gear Planner favorite icon (`assets/favorites/gear-planner.jpg`) | The Goldshire Wardrobe item icon from [Wowhead item 253527](https://www.wowhead.com/item=253527/goldshire-wardrobe), served from Wowhead's public image host. Blizzard owns the underlying World of Warcraft artwork. |
| Class favorite portraits (`assets/favorites/class-*.jpg`) | Cropped Wowhead screenshots of characters wearing the class tier helmets below. The screenshots are by the credited Wowhead contributors; Blizzard owns the underlying World of Warcraft artwork. |
| Icons and textures in the AZS addons | Read from the game client's own files by path (for example `Interface\Icons\...`) |
| Spell, zone, boss and class names | As the game names them |
| Creature models on the STATS page | Model numbers only (`assets/npc-models.txt`), from the **CMaNGOS Classic** world database: <https://github.com/cmangos/classic-db> |
| Gameplay in videos | Recordings of the game, by the people credited below |

Class favorite screenshot sources:

- Rogue — [Bloodfang Hood](https://www.wowhead.com/classic/item=16908/bloodfang-hood), screenshot 2808 by DJTyrant.
- Priest — [Circlet of Faith](https://www.wowhead.com/classic/item=22514/circlet-of-faith), screenshot 7492 by Deja.
- Warlock — [Hood of the Malefic](https://www.wowhead.com/tbc/item=31051/hood-of-the-malefic), screenshot 33791 by Miyari.
- Druid — [Cenarion Helm](https://www.wowhead.com/classic/item=16834/cenarion-helm), screenshot 58842 by Miyari.
- Warrior — [Onslaught Greathelm](https://www.wowhead.com/tbc/item=30974/onslaught-greathelm), screenshot 72439 by 130222.
- Paladin — [Judgement Crown](https://www.wowhead.com/classic/item=16955/judgement-crown), screenshot 78458 by Miyari.
- Hunter — [Dragonstalker's Helm](https://www.wowhead.com/classic/item=16939/dragonstalkers-helm), screenshot 877826 by Kolummon.
- Mage — [Arcanist Crown](https://www.wowhead.com/classic/item=16795/arcanist-crown), screenshot 58045 by Axolia.
- Shaman — [Earthfury Helmet](https://www.wowhead.com/classic/item=16842/earthfury-helmet), screenshot 1953 by eminent.

The stone-and-night-sky window, metal trim and button surfaces are original launcher art made by
`tools/make_launcher_art.py` or drawn in code. Sourced icons and portraits are credited above.

## Code

### Cloud sync: KeijinDE
The launcher's cloud sync (`src/Sync.cs`) is built on the sync framework by **KeijinDE**, used with his blessing.
Original project: <https://github.com/KeijinDE/ASDataSync>

```
MIT License

Copyright (c) 2026 KeijinDE

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Everything else
The launcher, the AZS addons (AZS Stats, AZS Handbook, AZS Minor Mods, AZSKit) and the tools are by Slimjim, under the
[MIT License](LICENSE).

## Addons in Download More

Each third-party addon belongs to its author. The copies on this project's `addons` release are mirrored from the
original download links in `addons/sources.txt`, so the launcher can download them all from one place. AZS addons
marked as built-ins are maintained in this repository and installed from the launcher.

| Addon | Author | Original |
| --- | --- | --- |
| AetherHUD | Rehabrocket | <https://github.com/NORehabRocket/AetherHUD> |
| Azerloot | Kindagrey | <https://github.com/Kindagrey/Azerloot> |
| Azeroth Cursor | Maidenhead | <https://github.com/Maidenquinn/AzerothCursor> |
| Azeroth FieldKit | Maidenhead | <https://github.com/Maidenquinn/azeroth-fieldkit> |
| Azeroth Stats | Maidenhead | <https://github.com/Maidenquinn/AzerothStats> |
| Big Cursor | JamieeLee | <https://github.com/JamieeLee/BigCursor> |
| BlackwellUI (BUI) | Blackwell | <https://github.com/Blackwellcode/BlackwellUI> |
| Cursor Reticle | Muffla | <https://github.com/Muffla/CursorReticle> |
| Damage Meter (AZS Spell Meter) | Kexo | <https://github.com/kex0/AZSSpellMeter> |
| Keijin UI | KeijinDE | <https://github.com/KeijinDE/KeijinUI_modern-as-ui> |
| LocalClock | JamieeLee | <https://github.com/JamieeLee/LocalClock-Addon> |
| LuckyOdds | Cadoky | <https://github.com/cadoky/LuckyOdds> |
| Move UI | JamieeLee | <https://github.com/JamieeLee/MoveUI> |
| Survivor UI + Survivor UI Classic | morderous | <https://github.com/morderous/survivor-ui> and <https://github.com/morderous/survivorui-classic> (bundled; GPL-3.0) |
| SurvivorsDragonUi | Mxgwyn | <https://github.com/migwynkriid13/SurvivorsDragonUi> |
| AZS Stats, AZS Handbook, AZS Minor Mods | Slimjim | this repository |

### Classic addons converted for this game
These are World of Warcraft 1.12 addons, rewritten by Slimjim to run in Azeroth Survivors' client. The original
authors keep their credit and their licenses.

| Addon | Original authors | Original | License |
| --- | --- | --- | --- |
| MinimapButtonBag | Tunhadil, continued by LaYt | <https://github.com/laytya/MinimapButtonBag-vanilla> | none stated by the original project; credited to its authors |
| BugSack and !BugGrabber | Fritti and Rabbit (credits: Rowne, Ramble, kergoth, ckknight, Gamefaq, thomasmo, damjau) | BugSack 2.1.0 / BugGrabber 2.1.1 for WoW 1.12 | Creative Commons BY-NC-SA 2.5 (`addons/BugSack/License.txt`); the converted version is shared under the same license |

## Community videos

Videos are never stored in this project or on your PC. They are streamed from YouTube when you play them, with the
creator's name and channel shown with each one.

| Creator | Channel | Where |
| --- | --- | --- |
| Holdherhand | <https://www.youtube.com/@Holdherhand> | Build Guides (11 class guides) |
| Des | <https://www.youtube.com/@Dessiq> | Build Guides (rogue, warrior) |
| Duke | <https://www.youtube.com/@duke-yt> | Build Guides (arcane mage) |
| Asmongold TV | <https://www.youtube.com/@AsmonTV> | Featured on the home screen |
| Sodapoppin and other creators | named on each video in the launcher | Featured on the home screen (`src/Community.cs`) |

The short how-to videos in the Guide (`guide-videos/`: camera distance, the DPS meter, luck, changing your race) are
recordings of the game by Slimjim.

## Fonts, logos and tools

- **The launcher's emblem** (`assets/logo.png`: the two cat heroes with their weapons over the shield, beside the
  title on the home screen) is **Slimjim**'s own art.
- **Source Serif 4** (the launcher's text font, `assets/fonts`, unchanged), by **Adobe**, under the SIL Open Font
  License 1.1: `assets/fonts/SourceSerif4-OFL.txt`. <https://github.com/adobe-fonts/source-serif>
- **The Discord logo** (`assets/discord`), from Discord's brand assets, used to link to the game's Discord.
- **Microsoft Edge** plays Community YouTube videos in the launcher's managed video window; Windows' own web view plays the Guide's videos.
- **Translations** (`assets/lang`) were machine-made for the launcher. Corrections from native speakers are welcome.
