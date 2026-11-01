# AZS Minor Mods HUD mobility worklist

This tracks the requested movable HUD elements and the limits established by the provided addon API documentation (`addon_docs(1).md`). The action bar, build rows and announcement banner are separate game elements; do not combine them just because the game originally groups or positions them together.

## Stored-levels “Level Up!” button

**Status: implemented locally; not verified in-game.** AZS Minor Mods now offers a movable replacement button, controlled by the `Replace Level Up button with movable version` setting. It uses `AZS_OpenStoredLevels()` to open the game's native choices and hides/restores the original through `AZS_SetHudShown("levels", ...)`. The button is a separate `MOVABLE` entry, so the existing Unlock UI handle provides move, resize, scale and optional snap behavior. Its two-line text shows current level and stored-level count. The API does not expose the native button's icon, so the replacement currently omits that icon instead of substituting an unrelated one. The native choice screen remains game-owned and unchanged.

Local package: `dist/AZSMinorMods-LevelUp-Movable-LocalTest.zip`.

## Six-spell / four-item build rows

**Status: investigation required before changing controls.** The supplied API documents `AZS_SetHudShown("build", shown)` as controlling the spells and jewelry rows together. It does not document separate show/hide controls, the item row's frame name, or item icon/name data. `AZS_SPELLS` describes spell IDs, ranks and cooldowns, but does not define a UI frame or the four item controls.

Requirements:

- Keep the six-spell row and four-item row independently movable, sizeable and individually hideable.
- Keep both separate from the action bar, which remains its own movable element.
- Preserve the native spell/item interactions and existing game visuals where possible.
- Identify the native row frames before adding handles. If the game exposes no separate frames, determine whether a faithful replacement can be built from documented data without losing item identity or functionality; do not silently hide both rows or invent game data.

## Event announcement banner (for example, “Hogger enters the battlefield!”)

**Status: implemented locally; not verified in-game.** The custom banner uses the API's localized `banner.text` and event metadata, rather than treating chat text as the sole source. It lasts 10 seconds total, fading in for 0.6 seconds and out for 0.6 seconds. Its own Unlock UI handle moves, resizes and scales it; boss events use the red style flag. It remains a separate element from “Move to Begin.”

The game API also exposes the same events to chat in some cases; chat is useful for comparison but isn't needed to reconstruct the announcement text. The addon's event-handler path receives the localized string directly, even while native banners are hidden. The frame clamps to the current screen after placement or resize while leaving larger saved coordinates intact for later display changes.

## Run clock, next-boss line, and active encounter bars

**Visibility fix and vertical sizing correction implemented locally; not verified in-game.** The run clock and next-boss line stay paired in one movable element. Custom encounter bars remain separate. Vertical bars keep tight spacing while distributing the chosen width evenly between however many live targets are present; their height remains resizable down to the minimum needed for the name and health bar. Saved absolute positions are clamped to the current screen without overwriting the saved larger-display coordinates. Encounter bars are re-clamped after their live width or height changes as targets come and go. The native clock and boss/guardian bars are restored when their replacements are not active.

## Delivery boundary

These changes remain local until explicitly requested for publication. Report in-game behavior as unverified until exercised in the game.
