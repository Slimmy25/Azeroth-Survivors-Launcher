# AZS Minor Mods HUD mobility worklist

This tracks the requested movable HUD elements and the limits established by the provided addon API documentation (`addon_docs(1).md`). The action bar, build rows and announcement banner are separate game elements; do not combine them just because the game originally groups or positions them together.

## Stored-levels “Level Up!” button

**Status: implemented locally; not verified in-game.** AZS Minor Mods now offers a movable replacement button, controlled by the `Replace Level Up button with movable version` setting. It uses `AZS_OpenStoredLevels()` to open the game's native choices and hides/restores the original through `AZS_SetHudShown("levels", ...)`. The button is a separate `MOVABLE` entry, so the existing Unlock UI handle provides move, resize, scale and optional snap behavior. It is hidden until `AZS_GetStoredLevels()` reports at least one unspent level, and its counter shows the full number waiting to be spent. The count is polled every 0.25 seconds so the button appears again as soon as a new point becomes available and disappears once all points are spent. The API does not expose the native button's icon, so the replacement currently omits that icon instead of substituting an unrelated one. The native choice screen remains game-owned and unchanged.

Local package: `dist/AZSMinorMods-LevelUp-Movable-LocalTest.zip`.

## Six-spell / four-item build rows

**Status: local implementation in progress; not verified in-game.** The six spell squares remain the game's existing `ActionButton1`–`ActionButton6` row, whose spell cooldown data is read from `AZS_SPELLS`. The four-item companion row is populated independently by polling the player's worn equipment slots 11–14 (two rings and two trinkets) and responding to inventory-change events. A bundled catalog generated from `assets/items.txt` supplies display names where an inventory link is unavailable; the actual equipped texture and native inventory tooltip come from the game.

The item row records each item's first observed equipped order in SavedVariables, so reloading or moving the same item between ring/trinket slots retains its place. On a fresh profile, already-worn items are ordered by the first slot scan (11 through 14); the game/API does not expose when the player originally acquired those items. The rows start attached with a small gap, can be detached from either row's right-click menu, then moved and sized independently. Each row can also be hidden through its own movable-feature controls.

The provided Aethus API documentation exposes `AZS_SetHudShown("build", shown)` for the native spell and jewelry build rows together, plus `AZS_SPELLS` for spell data. It does not document separate visibility controls or an item-acquisition history. This implementation does not claim to hide/replace the native jewelry row or provide an acquisition timestamp. In-game checks are still needed for the game's inventory texture/link APIs and for avoiding duplication with its own jewelry UI.

## Buff layout direction

The Buffs resize menu now offers horizontal/vertical layout, a separate Grow up/Grow down control, and adjustable icon spacing. Horizontal buffs wrap into additional rows in the selected direction; vertical buffs extend as a column in that direction. In vertical mode, resizing the frame height controls how many icons fit before the next column. This is implemented locally and still needs in-game verification.

## Event announcement banner (for example, “Hogger enters the battlefield!”)

**Status: implemented locally; not verified in-game.** The custom banner uses the API's localized `banner.text` and event metadata, rather than treating chat text as the sole source. It lasts 10 seconds total, fading in for 0.6 seconds and out for 0.6 seconds. Its own Unlock UI handle moves, resizes and scales it; boss events use the red style flag. It remains a separate element from “Move to Begin.”

The game API also exposes the same events to chat in some cases; chat is useful for comparison but isn't needed to reconstruct the announcement text. The addon's event-handler path receives the localized string directly, even while native banners are hidden. The frame clamps to the current screen after placement or resize while leaving larger saved coordinates intact for later display changes.

## Run clock, next-boss line, and active encounter bars

**Visibility fix, clock orientation, and layout padding implemented locally; not verified in-game.** The run clock and next-boss line stay paired in one movable element. Its right-click menu switches between a compact vertical stack (the default) and a horizontal line. Custom encounter bars remain separate and are now driven by run phase and `AZS_UNITS`, whether or not the player had the original boss HUD enabled; the earlier native-visibility gate could make the replacement disappear. Vertical bars distribute the chosen width evenly between however many live targets are present, preserve the names, and keep tight spacing. Their height remains resizable down to the minimum needed for the name and health bar. Padding controls are available for the addon's custom clock, encounter, tally, announcement, and buff layouts; native frames whose internal content layout is game-owned keep their native padding. Saved absolute positions are clamped to the current screen without overwriting the saved larger-display coordinates. Encounter bars are re-clamped after their live width or height changes as targets come and go. The native clock and boss/guardian bars are restored when their replacements are not active.

Local package: `dist/AZSMinorMods-UI-controls-local-2026-11-01.zip`.

## Tooltip

The tooltip is already in the movable list. While UI settings are unlocked, its transparent handle follows a visible `GameTooltip`; left-drag moves it and right-click opens its size/scale controls. The tooltip remains game-owned and keeps its own content. This implementation is local and still needs in-game confirmation with multiple tooltip types.

## Screen-edge placement and hiding a feature

Movable frames now clamp the visible child bounds rather than only the parent rectangle. This lets the action bar sit flush to the screen even when its empty parent has an inset around the actual buttons, and its scale is included in the edge calculation. Absolute saved positions use the same bounds. While UI settings are unlocked, Shift+left-clicking a handle or using **Hide feature** in its right-click menu makes the feature transparent and disables its mouse input where the frame API supports restoring it safely. Its handle remains gray and reachable so **Show feature** can restore it. The hidden choice persists across `/reload`. Implemented locally; edge behavior and mouse restoration still need in-game verification across the listed frame types.

## Delivery boundary

These changes remain local until explicitly requested for publication. Report in-game behavior as unverified until exercised in the game.
