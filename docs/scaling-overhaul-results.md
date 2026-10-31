# Display scaling overhaul: results

Status: implementation is complete on the isolated `feature/display-scaling-overhaul` branch. It has not been merged or published. The local preview executable is built with `AZS_DISPLAY_PREVIEW`; it has a separate instance identity and settings location and disables self-update and addon auto-update.

## What changed

- Added a single display-layout context for monitor work area, DPI, automatic density, and the saved manual interface-scale preference. Native per-monitor-v2 awareness is enabled before any form is created; `AutoScaleMode.None` prevents WinForms and the launcher from scaling the same controls twice. Geometry uses physical client pixels and logical fixed dimensions go through `Theme.Px`.
- Made the launcher resizable and responsive. Compact windows replace the right-side news/actions layout with a top menu; normal windows retain the existing composition. Footer, play/install, status and download progress remain anchored to the available client area.
- Converted guide, setup, options, stats, addons, community, changelog, gear, talent, and nested message/confirmation UI to respond to available size. Long content scrolls independently; minimum readable text is retained. Gear and talent panes preserve their shared class and edits across resize/DPI rebuilds. Open video hosts are resized instead of restarted.
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

Each case produced 33 rendered screenshots and passed 82 geometry/control checks (825 screenshots and 2,050 checks in total). The harness includes both builder views, setup slides, settings tabs, stats, addons, import/export overlays, gear set bonuses, ten favorites, nested dialogs, footer/actions, and long text. It also checks negative monitor-coordinate clamping and confirms that the active DPI-change path updates layout and keeps a dialog within its owner.

Additional validation on the same delivered EXE covered manual 80%, 125%, and 160% scale preferences (including the small-display cap); all nine class articles and both builders at 4K/100% and a constrained 1366×728/200% case; 4K/200%; and German, French, and Japanese text layouts. The two all-class stress cases each completed 110 alternating gear/talent rebuilds across all nine classes, with bounded GDI resources and no extra forms left open. Across the 25-case matrix and 10 extended cases, the harness produced 1,269 screenshots and passed 3,174 geometry, interaction, state-retention and converter checks. The process ran with native WinForms controls on this PC's actual 144-DPI PerMonitorV2 context; the other DPI/work-area combinations were simulated layout inputs.

The rendered screenshots were inspected at low-resolution/high-DPI, 1080p/144-DPI, and 4K at both 96 and 192 DPI. They show the expected compact-menu transition at constrained sizes, scrollable content, and anchored controls. The full case images and machine-readable check results are in the local validation output folder, not included in the source package.

## Limits before public merge

- This computer has one physical display. The suite exercised the same monitor-change layout/DPI handler synthetically, including a negative-coordinate display and an open modal, but did not physically drag the running launcher between two monitors with different Windows scaling. That hardware acceptance step remains open.
- The suite resizes the existing video host but does not validate actual video playback continuity against a live provider.
- Three translation files were sampled for wrapping; this is not a claim that every translated string in every bundled language has been linguistically reviewed.
- At very short/small sizes, lower content is scrollable by design. The harness validates reachability and geometry; it cannot replace a usability check on the target monitor.

Do not merge or publish until the user reviews the local preview and, if desired, performs physical mixed-monitor testing. Public main and release remain untouched.
