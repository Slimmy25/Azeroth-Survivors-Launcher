# Addon update investigation — 2026-11-03

## Findings

The catalog has 24 entries: 7 follow creator releases, 15 follow ZIP assets in the launcher's `addons` release, and 2 bundled projects are temporarily marked Work in Progress. A launcher mirror is not a live subscription to the author's repository: new author releases do not update that mirrored ZIP by themselves.

All seven direct release APIs returned matching ZIPs with SHA-256 digests during this audit. All 15 configured mirror assets were present and had GitHub digests. This confirms release metadata and catalog matching, not that every mirrored ZIP contains its author's newest code or that every addon was installed in a live game.

The prior updater fetched release metadata once per launcher session, silently discarded check failures, skipped some deferred checks without rescheduling, compared mirror timestamps with installation/file dates, and counted update attempts as successful updates. Downloading over a disabled addon could also create a second enabled folder; renaming it back collided with the old disabled directory.

## Implemented behavior

- Recheck installed managed addons periodically while the launcher remains open; retry deferred automatic checks after setup, game, sync or download operations finish. Updates never run while the game is running.
- Refresh and manual checks request fresh release metadata. The shared cache still serves ordinary installs.
- Update All and individual Update actions check the relevant source, install confirmed changes, and report successful updates, failed installs and check errors separately. A failure does not stop the rest of a batch.
- The Updates view shows each managed addon's status and whether it follows a creator release or the launcher mirror. Unknown/manual addons have no invented update URL.
- A diagonal Updates Available badge appears only for confirmed pending updates and opens Updates inside the secondary panel. Update All respects rollback pins; an individual pinned update asks before replacing the selected old copy.
- An unexpected ZIP name in the newest stable release is reported as a packaging error instead of silently substituting an older release.
- Creator updates compare tag plus digest. Mirror updates compare the exact asset identity, update timestamp and digest. Older installs lacking identity receive one verified refresh to establish that baseline.
- Verify and extract to staging first. Keep previous addon folders until every incoming folder is moved into place. Restore them if commit fails; preserve recovery files if rollback itself fails. Keep disabled addons disabled. SavedVariables directories are not part of these folder swaps.
- Integrity checks continue to use the exact hosting GitHub release asset's digest. SurvivorsForge remains a picture/review source.

## Catalog source inventory

| Addon | Update source |
|---|---|
| Azerloot By Kindagray | Launcher mirror: addons release |
| LuckyOdds By Cadoky | Launcher mirror: addons release |
| AZSSpellMeter By Kexo | Launcher mirror: addons release |
| AZS Max Hit By ghoulcompiler | Launcher mirror: addons release |
| Big Cursor By JamieeLee | Launcher mirror: addons release |
| Move UI By Jamie | Launcher mirror: addons release |
| MoveAnything By kodfod | Launcher mirror: addons release |
| SurvivorsDragonUi By Mxgwyn | Creator releases/latest |
| Azeroth Cursor By Maidenhead | Launcher mirror: addons release |
| Survivor UI + Classic By morderous | Launcher mirror: addons release |
| Keijin UI By Keijin | Creator releases/latest |
| AetherHUD By Rehabrocket | Launcher mirror: addons release |
| ASTinyPlates By Valorless | Creator releases/latest |
| BlackwellUI (BUI) By Blackwell | Launcher mirror: addons release |
| Azeroth FieldKit By Maidenhead | Creator releases/latest |
| Azeroth Stats By Maidenhead | Creator releases/latest |
| LocalClock By JamieeLee | Launcher mirror: addons release |
| Cursor Reticle By Muffla | Launcher mirror: addons release |
| ASBuilder By Cadoky | Creator releases/latest |
| Azeroth Hub By Maidenquinn | Creator releases/latest |
| AZS Handbook By Slimjim | Bundled; Work in Progress, unavailable |
| Slim Suite By Slimjim | Bundled; Work in Progress, unavailable |
| MinimapButtonBag By Tunhadil & LaYt | Launcher mirror: addons release |
| BugSack By Fritti & Rabbit | Launcher mirror: addons release |

## Verification and limits

The isolated preview harness checks release-identity changes, unchanged releases, rollback pins, failed-check handling, the badge, unmanaged addons, action-button separation, whisper data, disabled-addon replacement and preservation after a digest failure. Native WinForms screenshots are inspected at representative simulated layout sizes. These checks do not prove Steam Deck/Proton behavior, real mixed-monitor transitions or installation of all catalog addons into a user's live game.

Run the focused off-screen checks with `tools/Test-AddonUpdates.ps1 -Executable "dist/Azeroth Survivors Launcher - Addon Updates Preview.exe"`. The latest run passed 24 checks at each of 1920×1040 / 144 DPI and 1366×728 / 96 DPI. Screenshots use simulated layout contexts on the host DPI.
