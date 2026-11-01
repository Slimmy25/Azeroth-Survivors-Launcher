# Slim Suite HUD mobility worklist

This tracks the requested movable HUD elements and the limits established by the provided addon API documentation (`addon_docs(1).md`). The action bar, build rows and announcement banner are separate game elements; do not combine them just because the game originally groups or positions them together.

## Stored-levels “Level Up!” button

**Status: implemented locally; not verified in-game.** Slim Suite now offers a movable replacement button, controlled by the `Replace Level Up button with movable version` setting. It uses `AZS_OpenStoredLevels()` to open the game's native choices and hides/restores the original through `AZS_SetHudShown("levels", ...)`. The button is a separate `MOVABLE` entry, so the existing Unlock UI handle provides move, resize, scale and optional snap behavior. It is hidden until `AZS_GetStoredLevels()` reports at least one unspent level, and its counter shows the full number waiting to be spent. The count is polled every 0.25 seconds so the button appears again as soon as a new point becomes available and disappears once all points are spent. The API does not expose the native button's icon, so the replacement currently omits that icon instead of substituting an unrelated one. The native choice screen remains game-owned and unchanged.

Local package: `dist/AZSMinorMods-LevelUp-Movable-LocalTest.zip`.

## Six-spell / four-item build rows

**Status: implemented locally; not verified in-game.** The replacement has separate movable six-spell and four-jewelry rows. The spell row reads the six learned entries from `AZS_SPELLS` (the racial entry is last), resolves their canonical spell IDs against the 1.12 spellbook for icons/tooltips, and refreshes on the API message plus a polling fallback. The item row independently polls worn equipment slots 11–14 (two rings and two trinkets) and responds to inventory-change events. A bundled catalog generated from `assets/items.txt` supplies display names where an inventory link is unavailable; actual equipped textures and native inventory tooltips come from the game.

The item row records each item's first observed equipped order in SavedVariables, so reloading or moving the same item between ring/trinket slots retains its place. On a fresh profile, already-worn items are ordered by the first slot scan (11 through 14); the game/API does not expose when the player originally acquired those items. The rows start attached with a small gap, can be detached from either row's right-click menu, then moved, resized, scaled, snapped and oriented independently. The right-click layout control can make either row vertical without forcing the other to change. A setting replaces the native combined spell/jewelry rows through the documented `AZS_SetHudShown("build", shown)` API and can restore the user's prior visibility state. Each custom row can be hidden through its own movable-feature controls.

The provided Aethus API documentation exposes `AZS_SetHudShown("build", shown)` for the native spell and jewelry build rows together, plus `AZS_SPELLS` for spell data. It does not document separate native visibility controls or an item-acquisition history. In-game checks are still needed for the game's spellbook ID/icon mapping and inventory texture/link APIs.

## Encounter bar targeting and tooltip size

Encounter rows now accept clicks and call the game's exact-name target command for the displayed boss, guardian or elite. They do not use a nearest-enemy fallback. The row tooltip shows the unit role and current health. The shared game tooltip uses a consistent 260×140 default; the UI settings page and the in-world right-click controls can resize it. Neither behavior has been verified in-game yet.

## Buff layout direction

The Buffs resize menu now offers horizontal/vertical layout, a separate Grow up/Grow down control, and adjustable icon spacing. Horizontal buffs wrap into additional rows in the selected direction; vertical buffs extend as a column in that direction. In vertical mode, resizing the frame height controls how many icons fit before the next column. This is implemented locally and still needs in-game verification.

## Event announcement banner (for example, “Hogger enters the battlefield!”)

**Status: implemented locally; not verified in-game.** The custom banner uses the API's localized `banner.text` and event metadata, rather than treating chat text as the sole source. It lasts 10 seconds total, fading in for 0.6 seconds and out for 0.6 seconds. Its own Unlock UI handle moves, resizes and scales it; boss events use the red style flag. It remains a separate element from “Move to Begin.”

The game API also exposes the same events to chat in some cases; chat is useful for comparison but isn't needed to reconstruct the announcement text. The addon's event-handler path receives the localized string directly, even while native banners are hidden. The frame clamps to the current screen after placement or resize while leaving larger saved coordinates intact for later display changes.

## Run clock, next-boss line, and active encounter bars

**Visibility fix, clock orientation, and layout padding implemented locally; not verified in-game.** The run clock and next-boss line stay paired in one movable element. Its right-click menu switches between a compact vertical stack (the default) and a horizontal line. Custom encounter bars remain separate and are now driven by run phase and `AZS_UNITS`, whether or not the player had the original boss HUD enabled; the earlier native-visibility gate could make the replacement disappear. Vertical bars distribute the chosen width evenly between however many live targets are present, preserve the names, and keep tight spacing. Their height remains resizable down to the minimum needed for the name and health bar. Padding controls are available for the addon's custom clock, encounter, tally, announcement, and buff layouts; native frames whose internal content layout is game-owned keep their native padding. Saved absolute positions are clamped to the current screen without overwriting the saved larger-display coordinates. Encounter bars are re-clamped after their live width or height changes as targets come and go. The native clock and boss/guardian bars are restored when their replacements are not active.

Local package: `dist/AZSMinorMods-UI-controls-local-2026-11-01.zip`.

## Tooltip

The tooltip is in the movable list and now has one 260×140 default size applied after it is shown, preventing content changes from making it expand unpredictably. The UI settings page offers paired smaller/larger controls, and the in-world right-click controls still allow independent width, height and scale changes. It remains game-owned and keeps its own content. Verify with several tooltip types in-game because fixed height can clip unusually long descriptions until the player resizes it.

## Movable damage meter and layout controls

Slim Suite now has a separate movable damage-meter panel that reads `AZS_GetMeter()`'s documented line tuple (`name, damage, DPS, share, proc`) plus run total and DPS. It also accepts keyed rows. It hides and restores the native meter through `AZS_SetHudShown("meter", ...)`, rather than trying to move an undocumented native frame. Its Unlock UI handle provides move, resize, scale and global snap behavior. The draggable Done/control panel now contains grid visibility, 32/64/128/256 divisions, vertical and horizontal red center-line toggles, and global snapping; the main Slim Suite settings window closes when UI editing starts. These changes are local and still need in-game verification.

## 0.138.0 world-anchored combat text API review

Slim Suite currently does not draw floating combat-number text, so the new `AZS_GetCombatTextEvents()`, `AZS_ProjectCombatTextAnchors()` and `AZS_RegisterCombatTextFrameHandler()` APIs do not change an existing Slim Suite feature. They could support a future custom damage-number display that stays attached to the enemy or death location through camera movement, including targets without nameplates. The movable event-announcement banner is a different feature and remains unchanged.

## Screen-edge placement and hiding a feature

Movable frames now clamp the visible child bounds rather than only the parent rectangle. This lets the action bar sit flush to the screen even when its empty parent has an inset around the actual buttons, and its scale is included in the edge calculation. Absolute saved positions use the same bounds. While UI settings are unlocked, Shift+left-clicking a handle or using **Hide feature** in its right-click menu makes the feature transparent and disables its mouse input where the frame API supports restoring it safely. Its handle remains gray and reachable so **Show feature** can restore it. The hidden choice persists across `/reload`. Implemented locally; edge behavior and mouse restoration still need in-game verification across the listed frame types.

## Delivery boundary

These changes remain local until explicitly requested for publication. Report in-game behavior as unverified until exercised in the game.
