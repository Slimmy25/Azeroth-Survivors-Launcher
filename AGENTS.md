# Project instructions

- For current game behavior, the newest creator patch notes take priority over older patch notes and conflicting guide, addon, or launcher content. A newer note supersedes only the behavior it changes; retain unrelated earlier information.
- Preserve the creator's wording in game/developer patch-note entries. Do not paraphrase them. Keep older patch-note entries as historical records.
- Guide explanations outside developer patch notes may be paraphrased to match current behavior.
- Update affected shared data, generated Handbook content, addon behavior, and custom code together. Do not invent undocumented configuration keys, API signatures, or gameplay rules; ask the user when the intended change is unclear.

## WinForms layout: guide hierarchy and passage

- Control bounds are relative to their immediate parent. Before using `Left`, `Right`, or `Bounds` to position another control, check that both controls share the same parent. Convert coordinates explicitly if needed.
- `MakeThinScroll` returns the inner `ScrollPanel`, which is docked inside a bordered frame. Its `Right` is in the frame's coordinate system, not the outer guide panel's. Using `sp.Right` to position a sibling of the frame caused the persistent right-side dead space and partially hidden hierarchy labels.
- Anchor the hierarchy against the outer guide panel's right boundary (`PW`), which already excludes the separate favorites strip. Derive the passage width from that same boundary, the hierarchy width, and the intended gutter. Preserve the passage font size and the favorites boundary.
- The hierarchy column is borderless and anchored to the right boundary. Within that column, labels are left-aligned and children indent to the right, like Wikipedia's contents tree. Long labels wrap within its allocated width. Measure wrapped height using the largest font style the label will use, including its active bold state.
- When a visible gap persists, inspect the parent containers and coordinate systems before adjusting padding again. A successful build only confirms compilation; visually inspect the running build for alignment, wrapping, clipping, and scrolling before claiming those behaviors are verified. If inspection is interrupted, report exactly what remains unverified.

## Breadcrumb favorite heart

- Place the transparent favorite heart ABOVE the breadcrumb bar, horizontally centered on the hovered label's measured visible text glyphs. Exclude separators, arrows and padding. Use the same font, layout and trimming as the label's drawing; never hardcode anchors for particular words or place the heart beside the text.
- Convert the text and header bounds into the heart parent's coordinate system before positioning. Center the visible heart path, not an assumed control rectangle. Preserve a hover corridor between the label and heart, including the associated open dropdown; dismiss on leaving that group or launcher deactivation.
- `SideHead` in `src/Sections.cs` draws the breadcrumb and measures each label in the same paint pass. `crumbTextRects` supplies the favorite anchor; `stepRects` includes dropdown arrows for menu hover. Keep those roles distinct. Earlier labels may be hidden and long labels trimmed; anchor only to the actual visible label bounds.
- Hovering a breadcrumb opens its sibling-location dropdown; clicking a navigable ancestor goes to that location. The heart saves that breadcrumb's route, not whichever page happens to be current. Clicking a favorite opens it; hold left-click to reorder, right-click removes immediately. Keep transient menus/hearts inside the launcher's ownership and dismiss them when it loses focus.

## Guide content and navigation contract

- Maximize passage space with small intentional margins, retaining the established passage font size. Do not shrink passage text to accommodate navigation. Keep borders, scrollbar gutters and the separate favorites strip intact.
- Every class article follows the same consolidated contents hierarchy: [Class] → Spells; Talents → Talent Tree Builder; Gear → Items and Codex (Necklaces). [Class] Build Guides belongs under Additional resources. The Talent Tree Builder opens with the article's class selected. The talent calculator is a link, not an embedded talent-tree picture. Keep each class's own content, item data, and parent categories.
- Archive is a retained, flat alphabetical record of Codex passages. Do not include it in search suggestions or full-guide search results, and do not make links or favorites target it. Keep Codex entries in class guides; any Codex hyperlink elsewhere must lead to the relevant class-guide entry. Preserve the Archive text for reference and maintenance.
- Use themed text hyperlinks, spell/talent/Codex icons and in-article anchors instead of repeated large navigation buttons. Preserve cross-links between spells and talents and the universal Back action's previous scroll position in every class article. The tree stays visible and highlights the passage section currently in view.
- Wheel over the contents tree jumps one in-article section per complete notch (120 delta units); partial deltas accumulate for touchpads. Wheel down advances, wheel up returns, and endpoints do not wrap. This scrolls the passage and updates its highlight much faster than normal passage scrolling. External resources require clicking, so wheel browsing cannot unexpectedly leave the article. Do not add a visible scrollbar to the tree.
- Use the existing `WheelRouter` and `IWheel` interface for wheel handling over both tree labels and empty tree space; do not install an additional global mouse hook. Retain the fonts measured when building the sidebar when changing its active state, because `Theme.Zoom` resets after page construction.


## Display scaling contract

- `DisplayLayout` is the single source of monitor working area, DPI and effective UI density. Geometry passed to native controls is physical client pixels. Convert logical fixed dimensions with `Theme.Px`; do not multiply already measured or parent-relative dimensions again.
- Call `NativeDisplay.Enable` before creating any window. The launcher and nested Forms use `AutoScaleMode.None`. `WM_DPICHANGED` and the responsive layout coordinator own rebuilding; do not add a competing manifest/config autoscaler or stretch a screenshot of the whole UI.
- Theme fonts use `GraphicsUnit.Pixel`. When a subsystem deliberately paints in its own logical coordinate space (Armory), scope both `Theme.Zoom` and `Theme.DisplayScale`, restore them in `finally`, and convert pixel font sizes explicitly. Do not use screen-dependent `SizeInPoints` to rescale a pixel font.
- Capture sidebar fonts and indentation when creating a view. Later click/paint callbacks run after the section's Theme.Zoom scope has ended. Never recalculate them from the ambient root zoom.
- Short windows use a compact Menu, readable independent content scrolling and anchored build actions. All ten favorites must remain reachable. Do not shrink the passage merely to preserve a fixed desktop arrangement.
- Resize must retain draft gear, talent points, unsubmitted import text and scroll state. Resize existing video hosts rather than destroying/restarting their players. Menus/tooltips dismiss on root deactivation and stay within launcher ownership.
- `ScrollPanel` reading offsets are logical state: capture before rebuilding and restore after the new layout is constructed. In a compact display, move global actions to the top menu while keeping content independently scrollable and action controls anchored.
- `GuideContentsPanel` wheel browsing uses whole 120-unit notch steps with partial deltas accumulated; `Shift+wheel` scrolls the tree itself. Keep the active article section synchronized with the passage without adding a tree scrollbar.
- Do not create, dispose, or reparent native controls inside `OnPaint`. Queue structural UI changes onto the UI thread after painting; painting must stay a rendering-only operation.
- DPI and resize work should be exercised by the isolated `--preview-scaling` harness at representative working areas and DPIs. It creates real WinForms controls but simulates some display contexts; it cannot establish physical mixed-monitor movement or video playback.
- Never access `Control.Handle` from its early `WM_NCCALCSIZE`; use `Message.HWnd` to avoid recursive handle creation. Rebuilds requested during another panel build are deferred/coalesced.
- Build the isolated package with `CI=1 DISPLAY_PREVIEW=1`. It has separate settings and mutex identity and no public self-update/addon auto-update. Do not merge or publish it without the user's explicit instruction.
- `tools/Test-DisplayScaling.ps1` renders simulated resolution/DPI cases off-screen and exercises retained state and converter/save actions. These are actual native controls on the host DPI with simulated layout contexts, not proof of physical mixed-monitor testing. Keep that distinction in reports.
