# Changelog

## 0.2.1

Fix release for the issues found in a code review of 0.2.0. It was written without access to Unity and has **not** been compiled or run in an Editor yet; compile it and run the EditMode tests before relying on it.

### Added: replacing texture and audio files
- **Assets → RenderGuard → Track as Placeholder** (Project window context menu): registers any selected file (for example a PNG or WAV, which cannot carry a `PlaceholderTag`) as an asset-only record, whatever its name is.
- **Swap File In Place** for asset-only records with a linked replacement: backs up the placeholder, overwrites its file with the replacement's content and re-imports it. The placeholder's `.meta` is untouched, so its GUID and import settings stay the same and every scene, prefab and material that used it picks up the new content without being edited. **Restore Original File** undoes it from the backup; **Stop Tracking** removes a record. Supports textures, audio clips and text assets with the same file extension; models and other multi-asset files are excluded. Unity Undo cannot reverse a file swap; the backup does.
- New record field `lastSwapBackupPath`. New tests in `AssetSwapServiceTests.cs`.
- `Documentation~/UNITY_COMPATIBILITY.md`: what was checked against the Unity 6.3 LTS upgrade guide.

### Renamed: PlaceholderGuard → RenderGuard
- Product, package ID (`com.renderguard.core`), namespaces (`RenderGuard`, `RenderGuard.Editor`), assemblies, menu (**Tools → RenderGuard**), dashboard class (`RenderGuardWindow`), registry folder (`ProjectSettings/RenderGuard`), backup folder, export file names, log prefixes and documentation. Classes named after the concept of a placeholder (`PlaceholderTag` and friends) and all `.meta` GUIDs are unchanged.
- The registry is copied automatically from `ProjectSettings/PlaceholderGuard/registry.json` on first load (the old file is left in place). See `MIGRATING_FROM_PLACEHOLDERGUARD.md`.

### Upgrade safety
- Ship `.meta` files with fixed GUIDs. 0.2.0 had none, so each install got a random `PlaceholderTag` script GUID and replacing the folder turned existing tags into missing scripts. **See `UPGRADE_0.2.1.md` before replacing an existing install.**
- Registry schema is now 3. A missing `schemaVersion` (v1) is detected correctly, and any older registry is backed up to `registry.json.pre-v3-backup-<timestamp>.json` before its first save.

### Fixed
- Undo/Redo watcher: Undo no longer downgrades `verified` records, and a Redo can no longer mark a record `applied` just because its scene is not loaded. Only `applied` records in loaded scenes are considered; present tag IDs are collected once per pass.
- Tagged GameObjects can now link and verify a prefab whatever their category is (previously Audio, Texture, Material, Dialogue, Animation and Script tags could never be linked). Asset-only records still check their category.
- Readiness can reach 100%: records whose tag or asset no longer exists are flagged "removed from project" by a complete scan and count as resolved; asset-only records can be marked as replaced by hand; stale records can be removed.
- Scans no longer mark open scenes dirty, and an unchanged registry is no longer rewritten (timestamps and `lastSeenUtc` only move on real changes), so scans do not produce version-control diffs.
- Build validator: the block/warn switch is a project setting stored in the registry (was per-user `EditorPrefs`); the validator no longer writes the registry; build scenes with unsaved changes are reported and block when blocking is on.
- Heuristics match whole words: `Temple`, `Tempest`, `Tempo`, `Swipe` and `Door_Template` are no longer flagged. Folder hints no longer flag every object in a scene stored in a `Prototypes`/`Temp` folder (assets and prefab roots only). Prefabs are no longer suggested twice. Pending suggestions that disappear are pruned after a complete scan.
- Missing-reference detection now includes nested structs and arrays/lists, disposes `SerializedObject`, and reads the reference ID by reflection so it compiles on Unity versions that renamed `InstanceID` to `EntityId`.
- Hierarchy paths are unique (`[n]` suffix for same-named siblings, `/` escaped); objects are found by tag ID first, so Apply/Locate/Approve no longer pick the wrong sibling.
- Duplicate-ID warnings list where the copies are, ignore legitimate prefab instances, and the Reports tab can give extra scene copies new IDs.
- Linked replacements and asset-only records follow moves/renames through GUIDs. Re-linking or unlinking an `applied` record no longer resets it.
- Dashboard: registry is no longer re-read on every repaint; suggestions are paged (20) with a confidence filter; scans, scene operations and dialogs run on the next editor tick; Locate works for prefab findings and says when it opened a scene.
- `ApplyToScene` moves children from a snapshot and verifies it (it could previously loop forever if a reparent was refused); prefab-approval failures reload the registry so it matches the restored prefab.
- CSV export neutralises spreadsheet formulas and is written as UTF-8 with BOM. `CountByType`/`CountByScene` tolerate null data.

### Packaging
- License changed from MIT to the Apache License, Version 2.0 (`LICENSE` replaced, `"license": "Apache-2.0"` added to `package.json`).
- Test assembly uses `UNITY_INCLUDE_TESTS` and `nunit.framework.dll`; new EditMode tests for heuristics, hierarchy paths and the registry.
- Documentation moved to `Documentation~` so Unity does not import it. `package.json` gained keywords.

## 0.2.0

- Added replacement asset selection, linking, unlinking, and compatibility verification.
- Added confirmed scene replacement for scene-authored GameObjects, with child preservation and Unity Undo support.
- Added replacement history to the JSON registry.
- Added full scene-asset scanning for scenes under `Assets`, including unopened scenes.
- Added heuristic placeholder suggestions for GameObjects and selected asset types.
- Added suggestion approval/dismissal workflows and backups for prefab tagging.
- Added serialized missing-reference and missing-script reporting.
- Added repeated placeholder ID warnings.
- Added dashboard overview, readiness estimate, status/category/context analytics, search, replacement history, and JSON/CSV export.
- Added EditMode tests for readiness calculations.
- Migrates v1 registry data in memory to schema version 2 while preserving existing placeholder records.

## 0.1.0

- Initial placeholder tag, JSON registry, scanner, dashboard, and pre-build validation.
