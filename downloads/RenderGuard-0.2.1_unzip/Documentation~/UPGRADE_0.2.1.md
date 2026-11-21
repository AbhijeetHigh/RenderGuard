# Upgrade from PlaceholderGuard 0.2.0 to 0.2.1

> **Historical guide.** Written when the package was called PlaceholderGuard; it is now RenderGuard. To move to RenderGuard, follow `MIGRATING_FROM_PLACEHOLDERGUARD.md` instead (it includes the same `.meta` precaution).

## Read this first: keep your `PlaceholderTag` components

PlaceholderGuard 0.2.0 shipped **without `.meta` files**. When you copied it into `Packages`, Unity generated a random script GUID for `PlaceholderTag.cs`, and every scene and prefab that uses a tag stores that GUID. 0.2.1 ships fixed GUIDs, so simply deleting the old folder and copying the new one would give `PlaceholderTag.cs` a **different** GUID and every existing tag would show as a **Missing Script**.

### Method: keep the old `PlaceholderTag.cs.meta`

1. Save the project and close Unity.
2. Copy `ProjectSettings/PlaceholderGuard/registry.json` somewhere outside the project, and make sure your scenes and prefabs are committed to version control (or copy the project).
3. Copy `Packages/PlaceholderGuard/Runtime/PlaceholderTag.cs.meta` and `Packages/PlaceholderGuard/Runtime/PlaceholderGuard.Runtime.asmdef.meta` to a safe place. These hold the GUIDs your scenes and prefabs refer to.
4. Remove the old `Packages/PlaceholderGuard` folder and copy the new `PlaceholderGuard` folder into `Packages`.
5. **Before reopening Unity**, copy the two `.meta` files from step 3 over the ones in `Packages/PlaceholderGuard/Runtime/` (replace the new ones).
6. Reopen Unity, wait for compilation, and open a scene that has tags. You should see `Placeholder Tag` components, not "Missing Script".

(If you prefer not to delete the folder: copy the new files over the old folder and skip any `.meta` file that already exists; the result is the same.)

### If tags already show "Missing Script"

Do not save the scenes. Close Unity, revert the scenes/prefabs from version control, restore the old `PlaceholderTag.cs.meta` as above, and try again.

### Brand-new installations

Nothing special: copy the whole folder, keep the `.meta` files.

## What happens to the registry

On first load the registry is upgraded to schema 3 in memory. Before its first save it is copied to `registry.json.pre-v3-backup-<timestamp>.json` beside the original. Keep your manual copy until you have checked the dashboard.

- The build-blocking switch is now stored **in the registry** (a project setting). The old per-user `EditorPrefs` value is not read; it defaults to on. Check the Overview tab and set it the way your team wants it.
- Run **Scan Entire Project** after upgrading. It will flag records whose tag no longer exists as "removed from project", relocate records whose tag moved, and drop stale pending suggestions. Review the Placeholders tab afterwards.
- Do not keep both the old and new package folders in `Packages`.
