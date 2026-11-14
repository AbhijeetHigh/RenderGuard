# Changelog

## 0.2.0

- Naming cleanup: renamed Editor-side scanner, analytics, registry, replacement, suggestion, report, and build-validation services to use the `RenderGuard` prefix. No new features or behavior were added by this naming pass.
- Kept the `PlaceholderTag` component, `PlaceholderAssetType` enum, and placeholder registry model names stable to avoid unnecessary scene, script, and data compatibility changes.

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
