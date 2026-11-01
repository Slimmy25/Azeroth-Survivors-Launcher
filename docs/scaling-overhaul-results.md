# Display scaling overhaul: results

Status: implementation is complete on the isolated `feature/display-scaling-overhaul` branch. It has not been merged or published. The local preview executable is built with `AZS_DISPLAY_PREVIEW`; it has a separate instance identity and settings location and disables self-update and addon auto-update.

## What changed

- Added a single display-layout context for monitor work area, DPI, automatic density, and the saved manual interface-scale preference. Native per-monitor-v2 awareness is enabled before any form is created; `AutoScaleMode.None` prevents WinForms and the launcher from scaling the same controls twice. Geometry uses physical client pixels and logical fixed dimensions go through `Theme.Px`.
- Made the launcher resizable and responsive. Compact windows replace the right-side news/actions layout with a top menu; normal windows retain the existing composition. Footer, play/install, status and download progress remain anchored to the available client area.
- Resizing keeps the 1100:690 client-area design ratio from every edge and corner. Maximize fits and centers that shape inside the monitor work area instead of stretching the launcher across an ultrawide screen. The initial fixed-size setup was removed so resize controls are available from startup.
- Converted guide, setup, options, stats, addons, community, changelog, gear, talent, and nested message/confirmation UI to respond to available size. Long content scrolls independently; minimum readable text is retained. Gear and talent panes preserve their shared class and edits across resize/DPI rebuilds. Open video hosts are resized instead of restarted.
- Corrected a high-DPI class-guide defect found during screenshot review: spell rows now use the same captured section scale for their body text, icons, chips, row spacing, and click regions. Their talent/set/Codex references no longer collide at 200% DPI.
- Kept class-tree labels left aligned and nested, wrapped to full measured rows, and widened the tree modestly in proportion to UI density while retaining the favorites boundary and passage font size.
- Kept import text, gear, talent allocation, guide scroll/history, and favorites reachable through responsive rebuilds. Modal dialogs remain owned by the launcher, resize their text/layout with DPI, and dismiss or stay contained with the root window.
- Kept the guide contents sidebar aligned to the passage, using the outer guide boundary and preserving the favorites strip. Its wheel navigates one article section per complete notch; `Shift+wheel` scrolls the tree. Layout/font state is captured at construction, and the passage type size is not reduced to make room.
- Updated the developer test harness and local package isolation. The scaling preview is rendered off-screen; it does not open File Explorer or alter Windows display settings.
- Dimmed the idle Community "What's Next" preview, including its title, border, image and play affordance; hovering restores full contrast. It remains a selectable preview and does not start playback on its own.
- Corrected same-article Codex navigation so clicking a Codex link while already in its class guide jumps to that exact Codex entry.

## Validation performed

The acceptance harness ran against the final preview executable at 25 simulated work-area/DPI combinations:

| Simulated work area | DPI contexts |
| --- | --- |
| 1366×728 | 96, 120, 144, 168, 192 |
| 1920×1040 | 96, 120, 144, 168, 192 |
| 2560×1400 | 96, 120, 144, 168, 192 |
| 3840×2080 (4K work area) | 96, 120, 144, 168, 192 |
| 3440×1400 (ultrawide work area) | 96, 120, 144, 168, 192 |

The final 25-case matrix produced 34 rendered screenshots and passed 133 geometry/control checks per case (850 screenshots and 3,325 checks total). It covers 1366×728, 1920×1040, 2560×1400, 3840×2080 and 3440×1400 work areas at 96, 120, 144, 168 and 192 DPI. The harness includes both builder views, setup slides, settings tabs, stats, addons, import/export overlays, gear set bonuses, ten favorites, nested dialogs, footer/actions and long text. It also checks negative monitor-coordinate clamping, DPI suggested-rectangle parsing, one-time size conversion during DPI transitions, dialog containment after a DPI change, ratio-preserving edge/corner sizing, centered maximize geometry, queue ordering for skipped videos, active-filter queue separation, and the existing-client setup handoff rule. The 1,071 unique guide-link routes are checked for valid destinations or intentional hover-only behavior. The compact gear selector was exercised at its first, wrapped and last scroll positions; its clipped visible rows remained selectable. Save/load callbacks were checked through the rendered controls' reachable hit areas.

Extended isolated runs covered all nine class articles, class-specific spell/talent/gear/Codex jumps, and both builders at 4K/100% and constrained 1366×728/200% with a 160% interface preference. German and French were sampled at 1920×1040/144 DPI, and Japanese at 4K/100%. The current 4K/144-DPI stress run completed 110 alternating gear/talent rebuilds across all nine classes, with bounded GDI resources and no extra forms left open; it passed 271 checks and rendered 72 screenshots. The preceding five extended runs passed 1,137 checks and rendered 360 screenshots. The process ran with native WinForms controls on this PC's actual 144-DPI PerMonitorV2 context; other DPI/work-area combinations were simulated layout inputs.

Across the final matrix and the recorded extended runs, 1,282 screenshots were generated and 4,733 checks passed. Rendered samples were visually inspected at constrained/high-DPI, 1080p/144-DPI and 4K/144-DPI, including class articles, the gear builder and the muted Community preview. Captures use simulated display sizes on this PC and are not proof of physical movement between monitors. Final matrix CSVs and screenshots are in `build/display-scaling-final-qa`.

## Limits before public merge

- This computer has one physical display. The suite exercised the same monitor-change layout/DPI handler synthetically, including a negative-coordinate display and an open modal, but did not physically drag the running launcher between two monitors with different Windows scaling. That hardware acceptance step remains open.
- The suite resizes the existing video host but does not validate actual video playback continuity against a live provider.
- Three translated layouts were sampled for wrapping; this is not a claim that every translated string in every bundled language has been linguistically reviewed.
- The launcher's chosen window size is not persisted between launches; only the interface-size preference is saved. Persisting the dragged window dimensions/position could be a separate usability improvement.
- At very short/small sizes, lower content is scrollable by design. The harness validates reachability and geometry; it cannot replace a usability check on the target monitor.

Do not merge or publish until the user reviews the local preview and, if desired, performs physical mixed-monitor testing. Public main and release remain untouched.
