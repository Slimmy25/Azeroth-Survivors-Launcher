# Display scaling overhaul plan

Status: proposed after publishing commit 2f7ef9b. No scaling implementation is included in this plan.

## Goal

Readable UI on small laptops, 1080p, 1440p, ultrawide and 4K monitors without requiring changes to Windows display settings. Retain the launcher artwork, fonts, narrow guide borders and separate favorites rail. Fit controls to the available space while keeping text readable.

## Findings in the current source

- src/Launcher.cs creates a fixed-size window with FormBorderStyle.FixedSingle and ClientSize based on W/H.
- src/Controls.cs uses a fixed Theme.SectionZoom of 0.72 in secondary sections.
- app.manifest deliberately keeps Windows bitmap scaling because the UI mixes custom GDI+ drawing, native controls and fixed coordinates.
- Gear, talent, guide and popup layouts use several independently calculated pixel rectangles. Some fonts and custom controls capture zoom during construction.
- Simply enabling DPI awareness or enlarging the window will not resolve these independently computed layouts. The previous graphics branch demonstrated clipping and mismatched font/control sizes.

## Implementation sequence

1. Preserve the current release as the baseline. Begin implementation on an isolated scaling branch, with a separate local build and no automatic production update. Capture baseline views and interaction coordinates before changing the manifest.
2. Introduce one layout context per window: monitor DPI, monitor working area, desired readable UI scale, user preference and available content rectangle. All fonts, artwork, spacing and hit targets consume that context. Remove scattered scaling assumptions incrementally.
3. Coordinate DPI awareness with native WinForms controls and custom drawing. Draw at the target resolution rather than stretching the whole window bitmap. Rebuild size-dependent fonts and cached images when DPI changes. Check startup, monitor changes, minimize/restore and returning from a video.
4. Make the window resizable and responsive. Base the secondary panel, news, favorites and Play footer on their actual parent bounds. Keep minimum readable text sizes; when space is insufficient, reflow or scroll content instead of shrinking everything.
5. Convert the paired talent/gear tools first. Use one shared main/sidebar split, consistent padding, responsive talent grids, correct icon sizes, scrollable equipment/set descriptions and shared save controls. Ensure both views use the same class selector and peel position.
6. Apply the same layout rules to the Wiki, New Player Guide, setup slides, addon manager, stats, options and community/video panels. Keep action rows accessible on small screens. Update screenshots only after visual acceptance.
7. Rework tooltip/menu positioning from the same coordinates. Clamp them to their owning launcher pane or window as appropriate; hide them on deactivation. Verify that visual positions and clickable positions agree at every scale.
8. Add Auto plus optional user scale adjustment after automatic layout works. Auto should make sensible choices on both 4K at 100% and ordinary monitors, respect the working area, and remember manual overrides. Decide how to combine resolution and DPI from measured results, not a blanket 4K multiplier.

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

## Decisions to defer until prototypes

Minimum window size, compact-mode breakpoint, automatic 4K sizing and the precise range of manual scale choices require screenshots on representative displays. Do not promise support for every possible display configuration before validating those limits.
