# Display scaling overhaul: results

Status: implementation is complete on the isolated `feature/display-scaling-overhaul` branch. It has not been merged or published. The local preview executable is built with `AZS_DISPLAY_PREVIEW`; it has a separate instance identity and settings location and disables self-update and addon auto-update.

## What changed

- Added a single display-layout context for monitor work area, DPI, automatic density, and the saved manual interface-scale preference. Native per-monitor-v2 awareness is enabled before any form is created; `AutoScaleMode.None` prevents WinForms and the launcher from scaling the same controls twice. Geometry uses physical client pixels and logical fixed dimensions go through `Theme.Px`.
- Made the launcher resizable and responsive. Compact windows replace the right-side news/actions layout with a top menu; normal windows retain the existing composition. Footer, play/install, status and download progress remain anchored to the available client area.
- Converted guide, setup, options, stats, addons, community, changelog, gear, talent, and nested message/confirmation UI to respond to available size. Long content scrolls independently; minimum readable text is retained. Gear and talent panes preserve their shared class and edits across resize/DPI rebuilds. Open video hosts are resized instead of restarted.
- Corrected a high-DPI class-guide defect found during screenshot review: spell rows now use the same captured section scale for their body text, icons, chips, row spacing, and click regions. Their talent/set/Codex references no longer collide at 200% DPI.
- Kept class-tree labels left aligned and nested, wrapped to full measured rows, and widened the tree modestly in proportion to UI density while retaining the favorites boundary and passage font size.
- Kept import text, gear, talent allocation, guide scroll/history, and favorites reachable through responsive rebuilds. Modal dialogs remain owned by the launcher, resize their text/layout with DPI, and dismiss or stay contained with the root window.
- Kept the guide contents sidebar aligned to the passage, using the outer guide boundary and preserving the favorites strip. Its wheel navigates one article section per complete notch; `Shift+wheel` scrolls the tree. Layout/font state is captured at construction, and the passage type size is not reduced to make room.
- Updated the developer test harness and local package isolation. The scaling preview is rendered off-screen; it does not open File Explorer or alter Windows display settings.

## Validation performed

The acceptance harness ran against the final preview executable at 25 simulated work-area/DPI combinations:

| Simulated work area | DPI contexts |
| --- | --- |
| 1366×728 | 96, 120, 144, 168, 192 |
| 1920×1040 | 96, 120, 144, 168, 192 |
| 2560×1400 | 96, 120, 144, 168, 192 |
| 3840×2080 (4K work area) | 96, 120, 144, 168, 192 |
| 3440×1400 (ultrawide work area) | 96, 120, 144, 168, 192 |

Each case produced 34 rendered screenshots and passed 123 geometry/control checks (850 screenshots and 3,075 checks in total). The harness includes both builder views, setup slides, settings tabs, stats, addons, import/export overlays, gear set bonuses, ten favorites, nested dialogs, footer/actions, and long text. It also checks negative monitor-coordinate clamping, DPI suggested-rectangle parsing, one-time size conversion during DPI transitions, and dialog containment after a DPI change. The compact gear selector was exercised at its first, wrapped, and last scroll positions; its clipped visible rows remained selectable. Save/load callbacks were checked through the rendered controls' reachable hit areas.

Additional validation on the delivered EXE covered manual 80%, 125%, and 160% scale preferences (including the small-display cap); all nine class articles and both builders at 4K/100%, 4K/200%, and constrained 1366×728/200%; plus German, French, and Japanese layouts across all classes. Two all-class stress cases each completed 110 alternating gear/talent rebuilds across all nine classes, with bounded GDI resources and no extra forms left open. Across the 25-case matrix and 10 extended cases, the final suite produced 1,418 screenshots and passed 4,909 geometry, interaction, state-retention and converter checks. The process ran with native WinForms controls on this PC's actual 144-DPI PerMonitorV2 context; the other DPI/work-area combinations were simulated layout inputs.

The rendered screenshots were inspected at low-resolution/high-DPI, 1080p/144-DPI, and 4K at both 96 and 192 DPI, including the language and class articles. They show the compact-menu transition at constrained sizes, independently scrollable content, correctly spaced spell references, and anchored controls. The representative screenshots and machine-readable summary are in the local preview package; the full matrix remains in the workspace validation folder.

## Limits before public merge

- This computer has one physical display. The suite exercised the same monitor-change layout/DPI handler synthetically, including a negative-coordinate display and an open modal, but did not physically drag the running launcher between two monitors with different Windows scaling. That hardware acceptance step remains open.
- The suite resizes the existing video host but does not validate actual video playback continuity against a live provider.
- Three translation files were sampled for wrapping; this is not a claim that every translated string in every bundled language has been linguistically reviewed.
- At very short/small sizes, lower content is scrollable by design. The harness validates reachability and geometry; it cannot replace a usability check on the target monitor.

Do not merge or publish until the user reviews the local preview and, if desired, performs physical mixed-monitor testing. Public main and release remain untouched.
