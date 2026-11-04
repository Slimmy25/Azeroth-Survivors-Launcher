# Display scaling overhaul plan

Status: implementation and local QA are on `feature/display-scaling-overhaul`. Public main/release are unchanged. The launcher now keeps a 1100:690 design ratio while resizing and fits that shape into the monitor work area when maximized. Simulated display/DPI and navigation checks pass; physical mixed-monitor acceptance is still required before merging or release. See `scaling-overhaul-results.md` for measured results and limits.

## Goal

Readable UI on small laptops, 1080p, 1440p, ultrawide and 4K monitors without requiring changes to Windows display settings. Retain the launcher artwork, fonts, narrow guide borders and separate favorites rail. Fit controls to the available space while keeping text readable.

## Findings in the current source

- Before this overhaul, src/Launcher.cs created a fixed-size window. The current branch is resizable, ratio-constrained, and lays out controls against the responsive client area.
- src/Controls.cs uses a fixed Theme.SectionZoom of 0.72 in secondary sections.
- Baseline behavior before this overhaul relied on Windows bitmap scaling because the UI mixed custom GDI+ drawing, native controls and fixed coordinates. The current branch instead opts into native per-monitor awareness before creating windows and leaves automatic WinForms rescaling disabled.
- Gear, talent, guide and popup layouts use several independently calculated pixel rectangles. Some fonts and custom controls capture zoom during construction.
- Simply enabling DPI awareness or enlarging the window will not resolve these independently computed layouts. The previous graphics branch demonstrated clipping and mismatched font/control sizes.

## Work completed on this branch

- Work stayed on an isolated scaling branch with a separate local preview package and no public updates.
- DisplayLayout owns DPI, working area, automatic density and the saved interface-scale preference. Native per-monitor-v2 awareness is enabled before windows are created; `AutoScaleMode.None` avoids double scaling.
- Main-window edge/corner resizing preserves the 1100:690 client ratio. Maximize centers the same design shape inside the monitor work area. The compact layout, independent content scrolling and user-selected interface scale still handle smaller displays.
- The same responsive layout rules cover Guide/Wiki, New Player Handbook, setup, addons, stats, options, Community, changelog, Gear and Talent tools, and nested dialogs. Existing drafts, navigation state and live video hosts are retained during rebuilds.
- The community “What's Next” video preview is visually subdued and brightens on hover; it does not start playback until selected.
- The isolated preview harness checks all 1,071 current guide-link routes, class-specific spell/talent/gear/Codex destinations, in-article Back and favorite scroll restoration, plus the scaled UI matrix. The results file describes exactly what is simulated versus physically verified.

## Acceptance checks

- 1366x768, 1920x1080, 2560x1440, 3840x2160 and an ultrawide working area.
- Windows scaling at 100%, 125%, 150%, 175% and 200%; include 4K at 100%.
- Move the running launcher between monitors with different DPI and test monitors arranged left/above the primary display.
- Long guide titles, long translations, a populated ten-slot favorites rail, long gear names, several active set bonuses and all nine classes.
- Setup Next/Back/Cancel, class menus, equipment selection and wheel scrolling, peel switching, save/load, import/export overlays, tooltips, videos and Play remain reachable.
- Compare screenshots for clipping, text sharpness, unwanted gaps, border consistency and overlaps. Exercise the controls as well as inspecting screenshots.
- New installations and existing saved window/scale preferences both open on-screen with a usable window.

## Release gates and rollback

Approve each converted area in the isolated build before converting the next. A successful compile does not establish visual correctness. Keep production on the current layout until the acceptance checks pass. Release the scaling work only after explicit approval to merge/publish. The existing pre-overhaul main commit is def8301; the published feature baseline is 2f7ef9b. A local backup/before-tool-overhaul-20261031 tag preserves the earlier baseline.

## Remaining hardware check

The current minimum size, compact-mode breakpoint, automatic density and manual scale choices passed the simulated matrix. This machine has one physical monitor, so physically dragging the open launcher between two monitors with different Windows scaling remains unverified. Video playback continuity also requires a live-provider check.
