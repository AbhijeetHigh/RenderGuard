# RenderGuard 0.2.1

> **Formerly PlaceholderGuard.** If you used the package under its old name, read [`Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md`](Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md) before installing. Classes named after the concept of a placeholder (`PlaceholderTag`, `PlaceholderRecord`, ...) keep their names.

**A Unity Editor package for tracking temporary content, reviewing placeholder candidates, managing production replacements, understanding project readiness, and checking builds for explicitly tagged placeholders.**

RenderGuard is designed to make temporary development content visible and manageable. It combines explicit `PlaceholderTag` components with advisory name/folder heuristics, a local JSON registry, a replacement workflow, scan reports, a dashboard, and a pre-build validator.

> **Release status:** 0.2.1 is an early source-code release. It declares Unity 2021.3 as its minimum version. The missing-reference check resolves the serialized-reference API by reflection so the same source compiles on Unity versions that renamed `InstanceID` to `EntityId`. 0.2.1 was reviewed and edited without access to Unity, so it has **not** been compiled or run in an Editor yet. Compile it and run the EditMode tests in the exact Unity version and project where it will be used before relying on it. It was checked against Unity's 6.3 LTS upgrade guide on paper (see [`Documentation~/UNITY_COMPATIBILITY.md`](Documentation~/UNITY_COMPATIBILITY.md)); that is not a substitute for compiling it in 6.3.

---

## Contents

1. [What RenderGuard does](#1-what-renderguard-does)
2. [Feature summary](#2-feature-summary)
3. [Requirements and installation](#3-requirements-and-installation)
4. [First-time setup](#4-first-time-setup)
5. [End-to-end workflow](#5-end-to-end-workflow)
6. [How scanning and detection work](#6-how-scanning-and-detection-work)
7. [Replacement lifecycle and safety](#7-replacement-lifecycle-and-safety)
8. [Dashboard guide](#8-dashboard-guide)
9. [Build validation](#9-build-validation)
10. [Registry and data model](#10-registry-and-data-model)
11. [Complete package structure and file-by-file reference](#11-complete-package-structure-and-file-by-file-reference)
12. [Tests](#12-tests)
13. [Troubleshooting](#13-troubleshooting)
14. [Known limitations and out-of-scope features](#14-known-limitations-and-out-of-scope-features)
15. [Upgrading](#15-upgrading)
16. [Development notes](#16-development-notes)
17. [License](#17-license)

---

## 1. What RenderGuard does

Game projects often contain temporary models, prototype characters, greybox objects, test audio, draft textures, and other content that should eventually be replaced or consciously approved before release.

RenderGuard provides a workflow for:

1. **Marking known placeholders.** Add a `PlaceholderTag` component to a GameObject and describe why it is temporary.
2. **Finding possible placeholders.** Scan scenes, prefabs, and selected asset categories for names or folder paths that look temporary. These matches are suggestions, not confirmed placeholders.
3. **Recording intent.** Store placeholder identity, location, type, reason, replacement path, status, and history in a project-local JSON registry.
4. **Managing replacements.** Link a candidate production asset, verify the link, and—where supported—apply a prefab to a scene-authored GameObject after confirmation.
5. **Reviewing project state.** See counts, categories, locations, scan issues, and a readiness estimate in the dashboard.
6. **Protecting builds.** Warn or block a build when explicitly tagged, non-exempt placeholders remain in enabled Build Settings scenes.

### Design principles

- Explicitly tagged placeholders are authoritative records.
- Heuristic matches remain advisory until a developer approves them.
- Linking a replacement is not the same as applying it.
- Applying a replacement is a separate, confirmed action.
- Scene modifications are not saved automatically.
- RenderGuard does not silently delete or overwrite a production asset.
- Core scanning, registry, replacement management, analytics, reports, and build validation run locally in the Unity Editor. There is no cloud account or AI service in 0.2.1.

---

## 2. Feature summary

| Area | Included in 0.2.1 | Important boundary |
|---|---|---|
| Explicit tagging | `PlaceholderTag` component with ID, name, category, reason, and build exemption fields | Tags are attached to GameObjects, not arbitrary files |
| Project scan | Loaded and unopened scenes under `Assets`, prefab assets under `Assets`, and selected asset categories | Does not scan every Unity asset format or packages outside `Assets` |
| Suggestions | Whole-word name heuristics for GameObjects; name and folder heuristics for selected asset types and prefab roots | Suggestions can be false positives and require review |
| Missing references | Serialized object-reference checks on components in scanned scene/prefab hierarchies | Not a universal asset dependency validator |
| Replacement links | Select, link, verify, and unlink assets | Does not modify the placeholder by itself |
| Applying replacements | Confirmed prefab replacement of a scene-authored GameObject; in-place file swap for tracked textures, audio clips and text assets (same extension, backed up first) | Prefab instances and prefab contents are not auto-applied; models and other multi-asset files cannot be swapped in place |
| Undo | Uses Unity Undo for a scene replacement and listens for Undo/Redo to refresh state | Keep the operation in Unity's Undo history; still review and save the scene yourself |
| Analytics | Counts by status, type, and scene/asset path; readiness estimate; records whose tag or asset vanished are flagged as removed | A project-management estimate, not a quality guarantee |
| Reports | Current scan export to JSON or CSV | Export reflects the latest scan held in the dashboard |
| Build protection | Pre-build warning or blocking (project-wide setting) for explicit non-exempt tags in enabled build scenes; read-only scan | Suggestions, asset-only records, and broken-reference issues do not block builds; unsaved build scenes are reported |
| AI generation | Not included | Dialogue generation, TTS, image/model/animation generation are future work |

---

## 3. Requirements and installation

### Requirements

- Unity **2021.3 or newer** as declared in `package.json`.
- A Unity project with a normal `Assets`, `Packages`, and `ProjectSettings` directory structure.
- No external Python service, API key, database, or network connection is required for the 0.2.1 core package.
- Unity Test Framework is needed only to run the included EditMode tests.

The code contains a `UNITY_6000_4_OR_NEWER` conditional for the newer Unity 6.4 serialized-reference API. Other Unity-version-specific behavior should still be checked in the target editor.

### Install by copying the package folder

1. Close Unity before changing the package folder.
2. Extract the ZIP file.
3. Open the extracted folder and locate the inner folder named `RenderGuard`.
4. Open the Unity project root. In Unity Hub, use the project's three-dot menu and choose **Show in Explorer** (Windows), or open the project folder in your file manager.
5. Open the project's `Packages` folder.
6. Copy the entire inner `RenderGuard` folder into `Packages`.
7. Confirm that `package.json` is directly inside `Packages/RenderGuard/`.
8. Reopen Unity and wait for package import and script compilation.
9. Open **Tools → RenderGuard → Open Dashboard**.

Expected layout:

```text
YourUnityProject/
├── Assets/
├── Packages/
│   ├── manifest.json
│   └── RenderGuard/
│       ├── package.json
│       ├── Runtime/
│       ├── Editor/
│       ├── Tests/
│       └── Documentation~/
├── ProjectSettings/
└── Library/
```

**Important:** Copy the extracted folder, not the ZIP file. Every file ships with a `.meta` file; keep them. If you are replacing an existing PlaceholderGuard installation that already has `PlaceholderTag` components in your scenes or prefabs, read [`Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md`](Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md) first. Avoid an accidental nested path such as `Packages/RenderGuard/RenderGuard/package.json`. The correct path is `Packages/RenderGuard/package.json`.

### Alternative: Add from disk through Package Manager

If you do not want to copy the package folder manually, use Unity Package Manager's **Add package from disk** command and select this package's `package.json` file. Do not also copy the same package into `Packages`; use one installation method only.

---

## 4. First-time setup

### 4.1 Open the dashboard

Use **Tools → RenderGuard → Open Dashboard**. The window has four tabs:

- **Overview**
- **Placeholders**
- **Suggestions**
- **Reports**

The dashboard header contains two scan actions:

- **Scan Entire Project** — scans the wider project scope described below.
- **Scan Build Scenes** — scans only enabled scenes configured in Build Settings.

### 4.2 Tag a placeholder manually

1. Open a scene.
2. Select the GameObject that currently uses temporary content.
3. In the Inspector, choose **Add Component**.
4. Search for **Placeholder Tag** and add it.
5. Set the display name, reason, and asset category.
6. Decide whether it is genuinely allowed to remain in a build. If so, enable the exemption and provide a clear exemption reason.
7. Run **Scan Entire Project**.

`AssetId` is serialized and hidden in the Inspector. In the Editor, `OnValidate()` creates a GUID when the ID is empty and fills the display name from the GameObject name when the display name is empty.

### 4.3 Placeholder categories

`PlaceholderAssetType` contains:

| Value | Intended meaning |
|---|---|
| `Other` | General or uncategorized placeholder |
| `Audio` | Temporary audio content |
| `Texture` | Temporary texture content |
| `Model` | Temporary model or GameObject content |
| `Material` | Temporary material content |
| `Dialogue` | Temporary dialogue content |
| `Animation` | Temporary animation content |
| `Script` | Temporary script asset |

For a tagged GameObject the category is a descriptive label: any prefab (or model) can replace it, whatever the category says. For an asset-only record (created by approving an asset suggestion) the category is also used to check that the linked replacement is a compatible asset type. It does not infer what is inside an arbitrary asset.

---

## 5. End-to-end workflow

This is the recommended everyday workflow for a Unity developer.

### Step 1 — Scan the project

1. Open the dashboard.
2. Click **Scan Entire Project**.
3. Allow Unity to finish scanning. A progress bar is shown while scene and prefab assets are processed.
4. Check the **Reports** tab for tagged placeholders, suggestions, missing references, missing scripts, duplicate IDs, and scan warnings.

The project scan:

- Checks currently loaded scenes first.
- Finds and scans other scene assets under `Assets`, opening them additively where needed and closing scenes it opened after scanning.
- Scans prefab assets under `Assets` by loading prefab contents and unloading them after inspection.
- Checks names and paths for selected asset types.
- Saves the resulting registry data.

The project scan is broader than the build scan and may take time on large projects.

### Step 2 — Review explicit placeholders

Open the **Placeholders** tab. Each registered item displays its name, category, target kind, location, and replacement status.

Use the search field to filter by:

- Display name
- Scene or asset path
- Asset category
- Placeholder ID

Select **Manage Replacement** for the record you want to work on.

### Step 3 — Choose and link a replacement

1. Select a placeholder record in the **Placeholders** tab.
2. In **Replacement manager**, assign an asset to the **Replacement asset** object field. You can drag an asset from the Project window into this field.
3. Click **Link Replacement**.
4. RenderGuard checks that the selected asset is inside the project's `Assets/` folder, can be loaded, and is compatible with the placeholder category and target kind.
5. The path is stored in the registry and the status becomes `linked`.

**Linking does not change the scene.** It only records which production asset is intended to replace the placeholder.

### Step 4 — Verify the link

Click **Verify Link**. RenderGuard rechecks that the selected asset still exists and passes the same category and target compatibility checks.

A successful verification changes the state to `verified`, unless the record is already `applied`. Verification confirms the link; it does not prove that the asset is visually or functionally correct in the game.

### Step 5 — Apply a supported scene replacement

Automatic application is limited to a tagged GameObject that is a scene-authored object in a `.unity` scene.

1. Link a compatible prefab and verify it.
2. Select the placeholder record.
3. Click **Apply Replacement to Scene**.
4. Read the confirmation dialog and choose **Apply Replacement** only when ready.
5. RenderGuard instantiates the selected prefab in the recorded scene, copies the old object's local position, rotation, and scale, places it at the old sibling index, and reparents the old object's children under the new prefab.
6. The original tagged GameObject is removed through Unity's Undo system.
7. The scene is marked dirty, and the registry state becomes `applied` (the dashboard shows "applied (scene not saved)" until you save).
8. Review the changed scene and save it yourself when satisfied. Builds use the saved scene.

RenderGuard does **not** save the scene automatically. Use **Edit → Undo** or **Ctrl+Z** to revert while the replacement operation remains in Unity's Undo history. The package listens for Undo/Redo events to refresh the stored replacement status when it can detect the original tagged object in loaded scenes.

#### What automatic application does not copy

The operation preserves the GameObject's local transform, sibling position, and children. It does not copy arbitrary components, component values, animation state, runtime state, references from other objects, or custom data from the old GameObject onto the replacement prefab. Review those dependencies manually before saving.

#### Unsupported automatic-apply targets

- A GameObject that belongs to a prefab instance.
- The contents of a prefab asset.
- An asset-only placeholder record.
- A non-GameObject placeholder.

These targets can still be tracked and, where compatible, linked and verified. They must be updated deliberately by editing the appropriate source asset or prefab.

### Step 6 — Review suggestions

Open the **Suggestions** tab after a project scan.

Each suggestion shows:

- Candidate name
- Target kind (`gameObject` or `asset`)
- Asset category
- Heuristic confidence score
- Context path and, for GameObjects, hierarchy path
- A plain-language reason for the match

For each candidate:

- **Locate** selects or pings the candidate in Unity where possible.
- **Approve as Placeholder** records it as a placeholder after a confirmation dialog.
- **Dismiss** marks the suggestion as dismissed so it does not appear in the pending list.

Approval behavior depends on the target:

- **Scene GameObject:** adds a `PlaceholderTag` through Unity Undo, marks the scene dirty, registers the tag, and updates suggestion status. Save the scene yourself.
- **Prefab GameObject:** loads the prefab contents, creates a pre-change backup under `Library/RenderGuardBackups/`, adds the tag, and saves the prefab. If saving fails, the service attempts to restore the backup.
- **Standalone asset:** creates an asset-only registry record. It does not add a component to the file and is not currently a build blocker.

Review suggestions individually. A high confidence score is not proof that an asset is truly temporary.

### Step 7 — Review analytics and reports

Return to **Overview** for the project summary and readiness estimate. Use **Reports** to inspect the latest scan and export it as JSON or CSV.

The report is the latest scan kept in the open dashboard window. Run another scan to refresh it. Reopening the dashboard does not reconstruct a previous in-memory report automatically; the registry remains saved, but run a scan to populate a fresh report.

### Step 8 — Validate a build

Build validation runs automatically before a Unity build. By default, it blocks a build when non-exempt explicit `PlaceholderTag` components remain in enabled Build Settings scenes. The Overview tab contains a toggle to change between blocking and warning-only behavior; it is a project setting stored in the registry, shared with your team. Builds use the saved scene files, so save your scenes first.

A replacement link or verified link does not remove the original tag and therefore does not resolve the build finding. Apply the replacement and review the scene, remove the tag manually where appropriate, or use a documented exemption for a placeholder that is intentionally allowed to remain.

---

## 6. How scanning and detection work

### 6.1 Explicit tag detection

The scanner walks the transform hierarchy of each scanned scene and prefab, including inactive children. When it finds a `PlaceholderTag`, it registers or updates a `PlaceholderRecord` and creates a `tagged` finding.

Explicit tags are not inferred; they are deliberately added by a developer or through the suggestion-approval workflow.

### 6.2 Heuristic suggestions (names and folders)

Names are matched as **whole words**. A name is split on separators (`_`, `-`, spaces, dots), camelCase boundaries (`TempDoor` → `temp`, `door`), acronym boundaries (`WIPDoor` → `wip`, `door`) and letter/digit boundaries (`blockout01` → `blockout`, `01`). A word list is then matched against those words, so `Temple`, `Tempest`, `Tempo`, `Swipe` and `Door_Template` are **not** flagged, while `Temp_Door`, `WIPDoor` and `Placeholder_Crate` are.

| Words | Score |
|---|---:|
| `placeholder`, `placeholders`, `dummy`, `blockout` | 0.92 |
| `temporary`, `temp`, `proxy`, `greybox`, `graybox`, `prototype`, `wip` | 0.78 |
| Asset or prefab root stored in a directory whose name contains `placeholder(s)`, `temp`, `temporary`, `blockout`, `greybox`, `graybox`, `prototype(s)` as a word | 0.60 |

Folder hints apply only to **assets** and to a **prefab's root object** — never to every object inside a scene that merely lives in such a folder. Only directory names count, not the file name.

These values are **heuristic scores, not machine-learning probabilities**. The scanner reports the first matching rule and its reason. The heuristics live in `PlaceholderHeuristics` so they can be unit-tested.

A pending suggestion that a full project scan no longer finds is removed from the registry; approved and dismissed decisions are always kept.

### 6.3 Heuristic asset suggestions

Under `Assets`, the scanner checks the names and paths of these asset types:

| Unity search type | Suggestion category |
|---|---|
| `Texture2D` | `Texture` |
| `Material` | `Material` |
| `AudioClip` | `Audio` |
| `AnimationClip` | `Animation` |

Prefabs are not listed here: a prefab's root object is already suggested by the prefab scan, and tagging it is what makes it enforceable. A suggestion is created only when the name or path matches the heuristic rules. The scan counts the assets checked separately from the number of suggestions.

### 6.4 Missing script detection

For every GameObject hierarchy inspected in a scanned scene or prefab, the scanner checks its components. If Unity returns a null component entry, the scanner adds a `missing-script` issue with error severity.

### 6.5 Missing serialized object-reference detection

For non-null components, the scanner walks the serialized properties, **including nested structs/classes and arrays or lists of references** (arrays larger than 5000 elements, and arrays of plain values, are skipped for speed). It looks for object-reference properties, skips Unity's own bookkeeping fields (`m_Script`, `m_CorrespondingSourceObject`, `m_PrefabInstance`, `m_PrefabAsset`), and reports a `missing-reference` issue when Unity reports an underlying reference identifier even though the resolved object is null.

The identifier is read through reflection (`objectReferenceInstanceIDValue`, falling back to `objectReferenceEntityIdValue`), so the code compiles on Unity versions on both sides of that API rename. This is limited to serialized component properties in scanned scene and prefab hierarchies. It does not inspect every possible Unity asset format, script-generated reference, addressable catalog, external file, or every relationship in the project.

### 6.6 Duplicate placeholder IDs

Instances of a prefab share the prefab's tag ID by design, so they are **not** reported. A `duplicate-id` warning is added when two or more *independently authored* tags share an ID (scene objects that are not prefab instances, or separate prefab assets) — typically a copy/paste. The warning lists where they are. Such copies would otherwise be merged into one registry record.

In the Reports tab, **Give the extra scene copies new IDs** keeps the first object and gives the other scene copies a new ID (through Unity Undo; scenes are marked dirty, never saved). Copies inside prefab assets must be fixed by hand.

### 6.7 Hierarchy paths

Records and suggestions point at objects by a slash-separated hierarchy path. Siblings that share a name get a `[n]` suffix (`Root/Dummy[0]`, `Root/Dummy[1]`) so every path is unique, and a `/` inside a name is escaped as `%2F`. The tag ID, not the path, is the identity: Apply and Locate find an object by its ID first and use the path only to choose between several objects that share one ID.

### 6.8 Scan errors and warnings

If a scene or prefab cannot be opened or loaded for scanning, the scan records a `scan-error` warning and continues where possible. If an individual component cannot be inspected, an `inspection-warning` is recorded. Check the Console and the report's Issues section for details.

---

## 7. Replacement lifecycle and safety

### 7.1 Replacement states

A record's `replacementStatus` is intended to use these values:

| State | Meaning |
|---|---|
| `unlinked` | No replacement asset is currently linked |
| `linked` | A replacement asset path has been saved |
| `verified` | The linked asset currently exists and passes the compatibility checks |
| `applied` | RenderGuard completed the supported scene replacement, or the user marked an asset-only record as replaced |

A record can additionally be flagged **removed from project**: a complete project scan no longer finds its tag (or asset). This is not a status value; it is a flag (`missingFromProject`) and it counts as resolved. It is set only by a complete scan, only for records under `Assets/`, never for a scene or prefab that failed to scan, and never for `applied` records.

Typical progression:

```text
unlinked
   │ choose a compatible production asset
   ▼
linked
   │ click Verify Link
   ▼
verified
   │ confirm Apply Replacement (supported scene GameObjects only)
   ▼
applied
```

Unlinking returns a record to `unlinked`. Re-linking an existing record updates the stored path (and GUID) and sets it to `linked`; an `applied` record stays `applied` when it is re-linked or unlinked, because that does not undo what happened in the scene. The link is also stored as the replacement asset's GUID, so a moved or renamed replacement is found again on Verify and on every full scan.

**Important distinction:** `linked` and `verified` do not mean the placeholder has been removed from the scene. Build validation still sees the `PlaceholderTag` until the scene object is replaced or otherwise dealt with.

### 7.2 Compatibility checks

**Tagged GameObject records** (the normal case): the replacement must be a prefab or model asset (a `GameObject` main asset that is not a scene object), whatever the record's category is.

**Asset-only records** use the category:

| Placeholder category | Accepted replacement asset type |
|---|---|
| `Other` | Any main asset type |
| `Audio` | `AudioClip` |
| `Texture` | `Texture` |
| `Material` | `Material` |
| `Model` | `GameObject` |
| `Dialogue` | `TextAsset` or `ScriptableObject` |
| `Animation` | `AnimationClip` or `RuntimeAnimatorController` |
| `Script` | `MonoScript` |

For both kinds the asset must be inside the project's `Assets/` folder and loadable with `AssetDatabase.LoadMainAssetAtPath`. These are type checks, not semantic checks: a prefab can pass without being the correct object for the scene.

### 7.3 Undo, history, and saving

- Link, unlink, verify, apply, and detected Undo/Redo events are recorded in registry history.
- The scene apply action uses Unity's Undo API for its scene-object changes.
- The scene is marked dirty but never saved automatically.
- A scene replacement may open the recorded scene additively if it is not already loaded; it stays open. Review it and save it yourself. References from other objects to the replaced object are not repaired.
- The registry marks a record `applied` as soon as the replacement exists in the open scene. The dashboard shows "applied (scene not saved)" until the scene is saved. A build uses the saved scene file.
- The Undo/Redo watcher only ever touches records that are `applied`, and only when their scene is loaded, so an unrelated Undo (or an unloaded scene) cannot change any status. `verified` records are never downgraded by Undo.
- Keep normal project backups and version control. Unity Undo is not a substitute for source control or a project backup.

### 7.4 Tracking a texture or audio file from the Project window

A texture or audio file cannot carry a `PlaceholderTag`: components attach to GameObjects, and an asset only has import settings. (The blue label icon in the Inspector is Unity's own Asset Labels feature; RenderGuard does not read labels.) To track a file directly, select it (several at once is fine) in the Project window and choose **Assets → RenderGuard → Track as Placeholder**.

This creates an **asset-only record** whatever the file is called. The category is chosen from the asset's type (`Texture`, `Audio`, `Material`, `Animation`, `Script`, `Model`, otherwise `Other`). The record is keyed by the asset's GUID, so it follows the file if you move or rename it. Folders and anything outside `Assets/` are ignored. Asset-only records are advisory: they never block a build.

### 7.5 Swapping a tracked file in place

For an asset-only record that has a linked replacement, **Swap File In Place** (Placeholders tab → Replacement manager) replaces the placeholder with the replacement **without editing any scene, prefab or material**:

1. The placeholder file is backed up to `Library/RenderGuardBackups/<timestamp>-<name>`.
2. The placeholder file is made editable (this asks version control, for example Unity VCS, to check it out) and overwritten with the content of the replacement file.
3. Unity re-imports it. The placeholder's `.meta` file is untouched, so its **GUID and import settings stay the same**, and everything that referenced the placeholder now shows the new content.
4. The record becomes `applied`, the history logs the swap and the backup path, and the replacement file stays in the project (delete it later if nothing else uses it). You can rename the placeholder file afterwards; the GUID, and therefore the record, follow it.

**Restore Original File** copies the backup back and returns the record to `linked`.

Rules, enforced before anything is touched:

| Rule | Why |
|---|---|
| Asset-only records only | A tagged GameObject is replaced with Apply Replacement to Scene |
| Texture, audio clip or text asset | Plain single-file assets; models and other assets with sub-assets are excluded because overwriting them would break references to their sub-assets |
| Replacement must have the **same file extension** | The placeholder keeps its import settings and GUID; a PNG cannot become a JPG this way |
| Replacement passes the category check and is not the placeholder itself | Same type check as linking |

Things to know:

- **Unity Undo cannot reverse a file swap.** The backup (and your version control) is the way back. If copying or importing fails part-way, the original is restored automatically.
- The placeholder keeps its **old import settings** (for example texture Max Size or an audio clip's load type). Adjust them if the new file needs different ones.
- The swap only changes the project. It does not touch `Resources`, Addressables or builds; a placeholder file under `Resources` ships regardless of references.
- **Stop Tracking** removes an asset-only record (the file itself is never touched).

---

## 8. Dashboard guide

### Header controls

**Scan Entire Project** runs the comprehensive project scan.

**Scan Build Scenes** scans only enabled scenes in Build Settings.

The message area below the header displays success, warning, or error feedback for the most recent dashboard action.

### Overview tab

Displays:

- Readiness estimate
- Registered placeholders
- Unlinked records
- Linked records
- Verified links
- Applied replacements
- Explicitly exempt records
- Removed from project
- Pending suggestions
- Asset-only records
- Counts by category
- Counts by scene or asset path (up to the 12 most common contexts)
- Build-blocking toggle (a project setting stored in the registry)

The dashboard explains the readiness formula beside the score. Suggestions are not included in the score.

### Placeholders tab

Displays searchable placeholder records. Selecting **Manage Replacement** opens the replacement manager for that record.

The replacement manager contains:

- Selected placeholder name and ID
- Recorded target context and hierarchy path
- Replacement asset object field
- Link Replacement
- Verify Link
- Unlink (with confirmation)
- Apply Replacement to Scene (enabled only for supported scene GameObject records with a linked path)
- Locate
- Recent replacement history (up to 12 entries for that placeholder)
- **Remove Record** for a record flagged "removed from project"
- **Swap File In Place** and **Restore Original File** for asset-only records (see 7.5), **Mark as Replaced (done by hand)** for ones you swapped yourself, and **Stop Tracking** to remove a record

The record list and the suggestion list are cached views of the registry; the registry file is re-read only when it changed on disk (for example after a version-control update) or the window regains focus, not on every repaint. Scans, scene operations and confirmation dialogs run on the next editor tick rather than inside the GUI pass.

### Suggestions tab

Displays pending suggestions ordered by descending heuristic confidence, 20 per page, with a minimum-confidence slider. Each item includes the reason, location, category, and actions to approve, dismiss, or locate it.

### Reports tab

Displays the latest scan's:

- Scan scope and timestamp
- Tagged placeholder count
- Scene and prefab scan counts
- Candidate asset count checked
- Pending suggestion count
- Missing reference count
- Missing script count
- Duplicate ID count
- Findings and issues (duplicate-ID issues offer the new-ID fix)

Use **Export JSON** or **Export CSV** to save the current report through Unity's Save File dialog.

---

## 9. Build validation

`PlaceholderBuildValidator` implements `IPreprocessBuildWithReport`. Unity invokes it before a player build.

### Default behavior

Build blocking is enabled by default. The validator scans the enabled Build Settings scenes and examines the explicit tagged findings. **The validator never writes to the registry**; the scan it uses is read-only.

A finding blocks the build when:

- `exemptFromBuildBlock` is false; **or**
- it is marked exempt but `exemptionReason` is empty or whitespace.

A placeholder is treated as exempt only when both the exemption checkbox is enabled and a non-empty reason is provided.

A build uses the **saved** scene files. If an enabled build scene is loaded with unsaved changes, the validator reports it, and a blocking validator fails the build until the scene is saved or discarded (otherwise the scan could describe a scene that is not the one being built).

### Change blocking to warnings

Open **Tools → RenderGuard → Open Dashboard → Overview** and disable:

**Block builds when tagged placeholders remain in enabled build scenes**

This is a **project setting**: it is stored as `blockBuilds` in `ProjectSettings/RenderGuard/registry.json`, so the team and the build machine share it through version control. (0.2.0 kept it in per-user `EditorPrefs`; that old value is no longer read.)

When blocking is off, the build continues and RenderGuard logs a warning. When blocking is on, it throws `BuildFailedException` and stops the build.

### What does not currently block a build

- Pending or approved heuristic suggestions by themselves
- Asset-only placeholder records
- Missing object-reference issues
- Missing script issues
- Duplicate ID warnings
- Placeholders that exist only in prefabs, Resources or Addressables and are not part of an enabled scene

These are visible in scan reports but do not currently participate in the build-blocking decision.

---

## 10. Registry and data model

### Registry location

The local registry is stored at:

```text
ProjectSettings/RenderGuard/registry.json
```

The registry is plain JSON and is suitable for inspection and version control. A scan whose results did not change leaves the file byte-identical (timestamps only move when something really changes, and an unchanged registry is not rewritten), so routine scans no longer produce diffs. If the team needs to share decisions, replacement links and the build-blocking setting, include this file in source control and agree on how concurrent edits will be handled.

### Top-level schema

`PlaceholderRegistryData` uses schema version `3`:

```json
{
  "schemaVersion": 3,
  "blockBuilds": true,
  "placeholders": [],
  "suggestions": [],
  "history": []
}
```

### PlaceholderRecord fields

| Field | Purpose |
|---|---|
| `assetId` | Stable identity for a tagged GameObject or asset-only record |
| `displayName` | Friendly name shown in the dashboard |
| `assetType` | Category such as `Model`, `Audio`, or `Texture` |
| `targetKind` | `gameObject` for a tagged object; `asset` for a standalone asset record |
| `createdAtUtc` | UTC timestamp when the record was first created |
| `updatedAtUtc` | UTC timestamp of the latest registry update |
| `sceneOrPrefab` | Scene path, prefab path, or standalone asset path |
| `gameObjectPath` | Slash-separated hierarchy path for a GameObject record |
| `reason` | Why the item is considered temporary |
| `replacementAssetPath` | Unity project path to the intended replacement |
| `replacementAssetGuid` | GUID of the replacement, used to follow it if it is moved or renamed |
| `replacementStatus` | `unlinked`, `linked`, `verified`, or `applied` |
| `exemptFromBuildBlock` | Whether the developer has marked it as exempt |
| `exemptionReason` | Explanation required for a valid build exemption |
| `missingFromProject` / `missingSinceUtc` | Set by a complete project scan when the tag or asset no longer exists; counts as resolved |
| `lastSwapBackupPath` | Where the original placeholder file was backed up by an in-place swap (used by Restore) |

### PlaceholderSuggestion fields

| Field | Purpose |
|---|---|
| `suggestionId` | Stable suggestion identifier derived from the suggestion key |
| `suggestionKey` | Key used to update the same candidate on later scans |
| `targetKind` | `gameObject` or `asset` |
| `assetPath` | Candidate path when the target is a standalone asset |
| `contextPath` | Scene, prefab, or asset path that contains the candidate |
| `gameObjectPath` | Hierarchy path for a GameObject candidate |
| `displayName` | Candidate name |
| `assetType` | Suggested category |
| `confidence` | Heuristic score from the configured rules |
| `reason` | Explanation of the matched heuristic |
| `status` | `pending`, `approved`, or `dismissed` |
| `firstSeenUtc` / `lastSeenUtc` | Timestamps for first and most recent observation |

Existing suggestion decisions are preserved when the same candidate is found in later scans. After a complete project scan, pending suggestions that were not found again are removed; approved and dismissed suggestions are kept. `lastSeenUtc` moves only when a suggestion's content changes.

### PlaceholderHistoryEntry fields

History entries record:

- UTC action time
- Placeholder ID
- Action name
- Previous replacement path
- New replacement path
- Scene/prefab/asset context
- Human-readable details

History is for traceability. It is not a general asset versioning system and does not itself restore an asset.

### Registry migration and recovery

A registry whose `schemaVersion` is missing (v1) or lower than the current version is upgraded in memory when it is loaded; new fields take safe defaults. Before the first save that would overwrite it, RenderGuard copies the old file to `registry.json.pre-v3-backup-<timestamp>.json`. (In 0.2.0 the version field defaulted to the current version in code, so a v1 file without that field was not recognised as old and no backup was made.)

If JSON parsing fails, the package attempts to make a timestamped `.corrupt-...bak` copy and loads a new empty in-memory registry. This does not mean the old data was repaired. Check the Console and restore a known-good backup before continuing if the registry contains important data. Keep a manual backup before upgrading or editing registry files.

The registry save routine writes a temporary file and then replaces the current JSON file where supported.

### Other local data

| Location | Purpose | Source control guidance |
|---|---|---|
| `ProjectSettings/RenderGuard/registry.json` | Placeholder records, suggestions, and history | Commit if the team wants shared state |
| `Library/RenderGuardBackups/` | Backups created before approving a prefab suggestion | Normally do not commit; `Library` is generated/local data |
| `blockBuilds` inside `registry.json` | Project-wide build blocking switch | Shared with the registry |

---

## 11. Complete package structure and file-by-file reference

The package layout is:

```text
RenderGuard/
├── package.json
├── README.md
├── LICENSE
├── Runtime/
│   ├── PlaceholderTag.cs
│   └── RenderGuard.Runtime.asmdef
├── Editor/
│   ├── RenderGuard.Editor.asmdef
│   ├── Analytics/
│   │   └── PlaceholderAnalytics.cs
│   ├── Dashboard/
│   │   ├── AssetTrackingMenu.cs
│   │   └── RenderGuardWindow.cs
│   ├── Models/
│   │   └── PlaceholderRecord.cs
│   ├── Registry/
│   │   └── AssetRegistry.cs
│   ├── Replacement/
│   │   ├── AssetSwapService.cs
│   │   ├── PlaceholderIdService.cs
│   │   └── ReplacementService.cs
│   ├── Reports/
│   │   └── PlaceholderReportExporter.cs
│   ├── Scanner/
│   │   ├── PlaceholderHeuristics.cs
│   │   └── PlaceholderScanner.cs
│   ├── Suggestions/
│   │   └── PlaceholderSuggestionService.cs
│   └── Validation/
│       └── PlaceholderBuildValidator.cs
├── Tests/
│   └── Editor/
│       ├── AssetRegistryTests.cs
│       ├── AssetSwapServiceTests.cs
│       ├── PlaceholderAnalyticsTests.cs
│       ├── RenderGuard.Tests.asmdef
│       ├── PlaceholderHeuristicsTests.cs
│       └── PlaceholderPathTests.cs
└── Documentation~/
    ├── CHANGELOG.md
    ├── QUICK_START.md
    ├── MIGRATING_FROM_PLACEHOLDERGUARD.md
    ├── UNITY_COMPATIBILITY.md
    ├── UPGRADE_0.2.0.md
    ├── UPGRADE_0.2.1.md
    └── sample-registry.json
```

Every file and folder ships with a `.meta` file that has a fixed GUID (except `Documentation~`, which Unity ignores). This matters: without shipped `.meta` files Unity invents random script GUIDs for each install, and replacing the package folder would make every `PlaceholderTag` in your scenes and prefabs show as a missing script.

### Root files

#### `package.json`

Unity Package Manager manifest for the package.

It defines:

- Package ID: `com.renderguard.core`
- Version: `0.2.1`
- Display name: `RenderGuard`
- Description
- Declared minimum Unity version: `2021.3`
- Author metadata

Unity reads this file to recognize the folder as a package. It must sit directly in the package root.

#### `README.md`

This end-to-end guide. It explains installation, the intended workflow, dashboard, file architecture, registry, limitations, tests, and upgrade process.

#### `LICENSE`

The Apache License, Version 2.0 (the standard, unmodified text) governing use, copying, modification, distribution, the patent grant, and the warranty disclaimer. Keep this file when redistributing the package.

### Runtime files

#### `Runtime/PlaceholderTag.cs`

Defines the runtime-safe `PlaceholderTag` MonoBehaviour and the `PlaceholderAssetType` enum.

**Responsibilities:**

- Stores the serialized placeholder ID, display name, reason, category, build-exemption flag, and exemption reason.
- Exposes read-only properties for the stored values.
- Provides `Configure(...)` for setting the tag values from editor code or other code with access to the component.
- Uses `[DisallowMultipleComponent]` so a GameObject cannot have multiple copies of this tag through normal component addition.
- Uses `OnValidate()` under `UNITY_EDITOR` to generate a GUID if the ID is blank and default the display name to the GameObject name.

The component is intentionally small so it can remain attached to a scene or prefab GameObject. It does not itself scan, link, replace, or block a build; Editor-side systems perform those tasks.

The enum values are `Other`, `Audio`, `Texture`, `Model`, `Material`, `Dialogue`, `Animation`, and `Script`.

#### `Runtime/RenderGuard.Runtime.asmdef`

Assembly Definition for the runtime assembly named `RenderGuard.Runtime`. It has no Editor-only platform restriction and no additional assembly references. It keeps the runtime tag component in a separate assembly from UnityEditor-dependent code.

### Editor files

#### `Editor/RenderGuard.Editor.asmdef`

Assembly Definition for the Editor assembly named `RenderGuard.Editor`.

It references `RenderGuard.Runtime` and is restricted to the Editor platform. This prevents files that use `UnityEditor`, `AssetDatabase`, Editor windows, scene editor APIs, build callbacks, and Undo APIs from being compiled into player builds.

#### `Editor/Models/PlaceholderRecord.cs`

Declares the serializable data classes used by the registry, scanner, dashboard, analytics, and exporter:

- `PlaceholderRegistryData` — schema version plus placeholder, suggestion, and history lists.
- `PlaceholderRecord` — stored placeholder and replacement state.
- `PlaceholderSuggestion` — an advisory candidate and its review status.
- `PlaceholderHistoryEntry` — a timestamped audit entry for an action.
- `ScanReport` — metadata and collections from one scan.
- `ScanFinding` — a confirmed tagged placeholder finding.
- `ScanIssue` — a warning or error such as a missing reference or scan failure.

This file contains data models rather than Unity UI or scanning logic. Unity's `JsonUtility` serializes the registry/report model classes.

#### `Editor/Registry/AssetRegistry.cs`

Central persistence and record-management layer. It reads and writes `ProjectSettings/RenderGuard/registry.json`.

Key members:

- `RegistryDirectory` / `RegistryPath` — define the registry location. `UseRegistryPathForTests(...)` redirects it to a temporary file for tests.
- `Data` — lazy-loaded access to the cached registry data. `ChangeCounter` increments whenever the registry is reloaded or a changed registry is saved; the dashboard uses it to refresh cached views.
- `Load()` / `RefreshIfChangedOnDisk()` — read JSON, initialise lists/defaults, detect older schema versions (a missing `schemaVersion` counts as v1), and attempt a `.corrupt` backup if parsing fails; the second only reloads when the file changed on disk.
- `Save()` — skips the write entirely when nothing changed; otherwise writes through a temporary file and creates a `pre-v3-backup` copy first when upgrading an older file.
- `SetBlockBuilds(...)` — stores the project-wide build blocking switch.
- `Register(...)` — creates or refreshes a record from a `PlaceholderTag`. It writes to the tag only if the tag has no ID, only updates `updatedAtUtc` when something really changed, clears the removed flag if the tag is back, and (with `updateLocation`) keeps the first discovered context while preferring a scene over a prefab. Scans pass `updateLocation: false` and reconcile locations once at the end.
- `RegisterAsset(...)` — creates or updates an asset-only record using the Unity asset GUID.
- `LinkReplacement(...)` / `UnlinkReplacement(...)` — save or remove the replacement path and GUID; an `applied` record keeps its status.
- `HealReplacementPath(...)` — follows a moved or renamed replacement through its GUID.
- `SetReplacementStatus(...)`, `MarkResolved(...)`, `RemoveRecord(...)` — change status (recording the previous one), mark an asset-only record as replaced by hand, or delete a record while keeping its history.
- `AddHistory(...)` — appends a history entry to the in-memory registry; a later save persists it.
- `UpsertSuggestion(...)` — inserts a suggestion or refreshes a matching one, preserving its approved/dismissed decision and leaving `lastSeenUtc` alone unless content changed.
- `SetSuggestionStatus(...)`, `Find(...)` — mark a suggestion approved/dismissed and save; look up a record by ID.
- `ReconcileAfterFullScan(...)` — after a complete scan: relocates records whose tag moved, flags records whose tag or asset vanished (never for applied records, records outside `Assets/`, or contexts that failed to scan), follows moved asset-only records by GUID, and drops pending suggestions that were not seen again.

Other Editor systems should use this registry instead of writing the JSON file independently.

#### `Editor/Scanner/PlaceholderHeuristics.cs`

Public, unit-testable name and folder heuristics: `Tokenize(...)` splits a name into lower-case words (separators, camelCase, acronyms, letter/digit boundaries) and `Evaluate(...)` matches whole words and, when asked, directory names, returning a score and reason.

#### `Editor/Scanner/PlaceholderScanner.cs`

Implements the project and build-scene scans.

Key members:

- `ScanProject()` — reloads the registry, scans loaded scenes, all scene assets and all prefab assets under `Assets`, checks Texture/Material/Audio/Animation candidate assets, and — only if the scan completed — reconciles the registry (flags vanished tags, relocates moved ones, prunes stale pending suggestions). It then adds duplicate-ID issues and saves.
- `ScanBuildScenes()` — scans only enabled Build Settings scenes. **Read-only**: it never writes the registry. It adds a `scene-unsaved` issue for a loaded build scene with unsaved changes.
- `ScanScenePath(...)` — opens a scene additively when it is not loaded, scans it, and closes it if the scanner opened it; a failure marks that scene as failed so none of its records are flagged missing.
- `ScanPrefabAssets(...)`, `ScanHierarchy(...)` — scan prefab contents and hierarchies including inactive objects; register explicit tags (without moving record locations), record where each ID was seen, and suggest untagged objects.
- `InspectMissingReferences(...)` — missing scripts and missing serialized references, including references inside nested structs and arrays; `SerializedObject` instances are disposed.
- `ScanCandidateAssets(...)` — asset suggestions (names and folders) for Texture2D, Material, AudioClip and AnimationClip.
- `AddDuplicateIdIssues(...)` — reports independently authored tags that share an ID, listing their locations; prefab instances are exempt.
- `GetHierarchyPath(...)`, `BuildPathMap(...)` — unique hierarchy paths (`[n]` suffix for same-named siblings, `/` escaped as `%2F`); `BuildPathMap` computes a whole hierarchy in linear time.
- `FindTransformByPath(...)`, `FindTaggedTransform(...)` — locate an object by path, or by tag ID with the path only breaking ties; the latter refuses to guess when ambiguous.
- `StableId(...)` — deterministic MD5-based ID for suggestion identity, not a security credential.

#### `Editor/Suggestions/PlaceholderSuggestionService.cs`

Implements the user-approved transition from a suggestion to a tracked placeholder.

Key members:

- `Approve(...)` — dispatches approval based on target kind and context path.
- `ApproveSceneSuggestion(...)` — opens the scene additively if needed, finds the GameObject by its unique hierarchy path, refuses if it already has a tag, adds a tag through Unity Undo, configures/registers it, marks the scene dirty, and updates the suggestion status.
- `ApprovePrefabSuggestion(...)` — loads prefab contents, finds the target, writes a pre-change backup under `Library/RenderGuardBackups/`, adds/configures the tag, saves the prefab, and attempts to restore the backup if saving fails (in which case the in-memory registry is reloaded from disk so it cannot disagree with the restored prefab).
- `Dismiss(...)` — stores the dismissed suggestion state.

This service does not auto-approve all suggestions. The dashboard asks for confirmation before calling it.

#### `Editor/Replacement/ReplacementService.cs`

Contains validation, verification, application, and Undo/Redo status support for replacement operations.

Key members:

- `ValidateReplacement(...)` — a tagged GameObject needs a prefab or model asset (its category is only a label); an asset-only record is checked against its category.
- `VerifyLink(...)` — follows a moved replacement by GUID, reruns validation and marks the link `verified` unless the record is already `applied`.
- `ApplyToScene(...)` — validates, opens the target scene additively if needed (it stays open), locates the tag by ID (path only breaks ties; refuses ambiguous matches and prefab instances), instantiates the prefab, preserves local transform, sibling order and children (from a snapshot, with a check that every child moved), removes the old object through Unity Undo, marks the scene dirty, records history and sets the state to `applied`.
- `ReplacementUndoWatcher` — on Undo/Redo, only considers `applied` records (or a record whose last history entry is `undo-detected`) whose scene is loaded, and builds the set of present tag IDs once per pass. It never downgrades `verified` records.

The service never calls `EditorSceneManager.SaveScene`. Saving remains the developer's responsibility.

#### `Editor/Replacement/AssetSwapService.cs`

In-place file swap for asset-only records (see 7.5).

- `PlaceholderPathOf(...)` / `ReplacementPathOf(...)` — current paths, found through the GUIDs first so moves and renames are tolerated; they never modify the record.
- `CanSwap(...)` — every precondition (asset-only, replacement linked and present, category check, supported type, same extension, not the same asset) with a human-readable reason when it fails.
- `DescribeSwap(...)` — the text for the confirmation dialog.
- `SwapInPlace(...)` — backup, `AssetDatabase.MakeEditable`, overwrite, forced re-import, then status `applied` and history. On any failure after the overwrite started, the original is copied back.
- `CanRestore(...)` / `RestoreOriginal(...)` — copy the backup back, re-import, return the record to `linked`.

#### `Editor/Replacement/PlaceholderIdService.cs`

`RegenerateId(...)` gives one scene-authored tag a new ID through Unity Undo (scene marked dirty, never saved). Used by the Reports tab to fix copy/pasted tags that share an ID. Prefab assets and prefab instances are refused.

#### `Editor/Analytics/PlaceholderAnalytics.cs`

Calculates dashboard counts from registry data.

- `PlaceholderAnalyticsSummary` holds total, unlinked, linked, verified, applied, exempt, removed, pending-suggestion, asset-only, and readiness values.
- `BuildSummary(...)` calculates those values and the readiness score; `IsResolved(...)` is the single definition of "resolved".
- `CountByType(...)` / `CountByScene(...)` group records by category or context; both tolerate null data.

Readiness formula:

```text
resolved = applied records (including asset-only records marked replaced)
         + records explicitly exempted with a non-empty exemption reason
         + records removed from the project (tag or asset no longer exists)

readiness = round(100 × resolved / all tracked records)
```

If no records exist, readiness is `100`. A `linked` or `verified` record is not counted as resolved. Pending suggestions are not included in the denominator.

#### `Editor/Reports/PlaceholderReportExporter.cs`

Exports the most recent `ScanReport` supplied by the dashboard.

- `ExportJson(...)` opens a Save File dialog and writes the report using Unity `JsonUtility` with readable indentation.
- `ExportCsv(...)` opens a Save File dialog and writes findings, issues, and suggestions as CSV rows.
- `Csv(...)` wraps cell values in quotes, escapes embedded quotes, and prefixes cells that start with `=`, `+`, `-`, `@`, tab or CR with an apostrophe so Excel does not evaluate them as formulas. The CSV is written as UTF-8 with a BOM.

The exporter does not run a scan itself. Run a scan first so the dashboard has a report to export.

#### `Editor/Dashboard/AssetTrackingMenu.cs`

The **Assets → RenderGuard → Track as Placeholder** context menu (see 7.4). `SelectedAssetPaths()` returns the selected files inside `Assets/` (folders ignored), `CategoryFor(...)` picks the category from the asset's main type, and the action registers each file through `AssetRegistry.RegisterAsset` with the history action `tracked-from-project-window`.

#### `Editor/Dashboard/RenderGuardWindow.cs`

Implements the custom Unity Editor dashboard and connects UI actions to the other services.

- Menu entry: **Tools → RenderGuard → Open Dashboard**.
- `OnEnable()` / `OnFocus()` refresh the registry only if its file changed on disk.
- `OnGUI()` draws the header and routes to the selected tab.
- `DrawHeader()` renders the scan buttons and status message.
- `DrawOverview()` renders readiness, status counts, category/context counts, and the build blocking toggle.
- `DrawPlaceholders()` renders searchable records and the replacement manager.
- `DrawHistory(...)` shows up to 12 recent history entries for a selected placeholder.
- `DrawSuggestions()` presents pending suggestions and approve/dismiss/locate actions.
- `DrawReports()` displays scan totals, findings, issues, and JSON/CSV export controls.
- `Defer(...)` runs scans, scene operations and confirmation dialogs on the next editor tick instead of inside the GUI pass.
- `RunProjectScan()` and `RunBuildScan()` invoke the relevant scanner methods.
- `SelectRecord(...)` selects a placeholder for replacement management.
- `LocateRecord(...)`, `LocateSuggestion(...)` and `LocateInContext(...)` select or ping an asset, a prefab asset, or a scene GameObject (found by tag ID, then by path); they tell you when a scene was opened additively.
- `FixDuplicateIds(...)` applies the new-ID fix for a duplicate-ID issue.
- `SetMessage(...)` updates dashboard feedback.
- `Contains(...)` performs case-insensitive filtering for the search field.

The dashboard is a Unity Editor window, not a separate web app. Its latest scan report is held by the window until another scan is run or the window/session is recreated.

#### `Editor/Validation/PlaceholderBuildValidator.cs`

Implements `IPreprocessBuildWithReport` and runs before a Unity player build.

`OnPreprocessBuild(...)`:

1. Reads the project-wide `blockBuilds` setting from the registry (default `true`).
2. Calls `PlaceholderScanner.ScanBuildScenes()` (read-only).
3. Filters findings so only explicit tags with a valid documented exemption are allowed through.
4. Logs success when there are no blocking findings and no unsaved build scenes.
5. Throws `BuildFailedException` when blocking is enabled and unresolved tagged placeholders remain, or an enabled build scene has unsaved changes.
6. Logs a warning and allows the build to continue when warning-only mode is selected.

It does not currently fail a build for heuristic suggestions, asset-only records, missing references, missing scripts, or duplicate IDs.

### Tests

#### `Tests/Editor/` test files

- `PlaceholderAnalyticsTests.cs` — readiness: empty registry, applied/exempt/removed records, exemption without a reason, null-tolerant counters.
- `PlaceholderHeuristicsTests.cs` — `Temple`/`Tempest`/`Swipe`/`Door_Template` are not flagged, `Temp_Door`/`WIPDoor`/`Placeholder_Crate` are; folder rules; tokenizer.
- `AssetRegistryTests.cs` — v1 registry backup before upgrade, unchanged saves do not rewrite the file, the block-builds setting persists, repeated `Register` leaves a record untouched, reconcile (vanished/applied/out-of-scope/failed/moved records, stale suggestions), mark-replaced and remove. All of these use a temporary registry file, never the project's.
- `AssetSwapServiceTests.cs` — creates two small PNG assets under `Assets/__RenderGuardTests__` (deleted afterwards): the swap keeps the GUID, replaces the bytes and backs up the original; restore brings it back; different extensions, missing links and tagged-GameObject records are refused; the tracking menu's categories. These do touch the Asset Database, though never your real assets or registry.
- `PlaceholderPathTests.cs` — unique sibling paths, `/` escaping, find by path, find by tag ID with path as tiebreaker and no guessing.

These do not cover scanning real scene files, build callbacks, `ApplyToScene` or Undo/Redo detection; those still need manual testing in the Editor.

#### `Tests/Editor/RenderGuard.Tests.asmdef`

Defines the Editor test assembly. It references the Unity test runner assemblies, `RenderGuard.Runtime` and `RenderGuard.Editor`, uses `nunit.framework.dll` as a precompiled reference and the `UNITY_INCLUDE_TESTS` define constraint. `autoReferenced` is false so the tests remain a separate test assembly.

### Documentation files

#### `Documentation~/QUICK_START.md`

Short practical setup and usage instructions: install, tag a placeholder, link and apply a replacement, review suggestions, export reports, and understand build validation.

#### `Documentation~/UNITY_COMPATIBILITY.md`

What was checked against Unity's 6.3 LTS upgrade guide, the one version-sensitive API (the `InstanceID` / `EntityId` property read by reflection), the APIs the package relies on, and what to run to confirm it on 6.3.

#### `Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md`

What the rename from PlaceholderGuard changed (package ID, namespaces, assemblies, menu, registry folder), what it deliberately did not change, and the step-by-step migration, including the automatic one-time copy of the old registry.

#### `Documentation~/UPGRADE_0.2.1.md`

Historical guide for the PlaceholderGuard 0.2.0 → 0.2.1 upgrade (written under the old name), including how to keep existing `PlaceholderTag` components from turning into missing scripts.

#### `Documentation~/UPGRADE_0.2.0.md`

Upgrade instructions from 0.1.0 to 0.2.0, including backing up the registry, replacing the package folder, and how schema version 1 is migrated.

#### `Documentation~/CHANGELOG.md`

Release-by-release summary of features introduced in 0.2.0 and the original 0.1.0 foundation.

#### `Documentation~/sample-registry.json`

Example schema version 3 registry containing a sample GameObject placeholder, linked replacement path, status, and empty suggestion/history lists. It is illustrative data; do not overwrite your real registry with it.

---

## 12. Tests

The package includes EditMode tests for readiness, the heuristics, hierarchy paths and the registry (migration, no-op saves, reconciliation). They have not been run in an Editor yet. They do **not** cover scanning real scene files, replacement application, prefab approval, report export, build callbacks or Undo/Redo detection; test those by hand (see below).

### Enable the test assembly

1. Ensure Unity Test Framework is installed through Package Manager if it is not already available.
2. Open `Packages/manifest.json`.
3. Add the package ID to the existing `testables` array without removing the project's other dependencies or settings. For example:

```json
{
  "dependencies": {
    "com.unity.test-framework": "<your-installed-version>"
  },
  "testables": [
    "com.renderguard.core"
  ]
}
```

This shows the `testables` entry only for illustration. Preserve your project's existing dependencies and their versions; do not replace your manifest with this example. If Unity Test Framework is already installed, keep its current version.

4. Save the manifest and let Unity resolve packages.
5. Open **Window → General → Test Runner**.
6. Select **EditMode** and run the RenderGuard tests.

If Unity does not show the test assembly, check the Console, confirm the package ID in `testables` exactly matches `package.json`, and verify Unity Test Framework is installed.

### Testing recommendation before a real project build

Use a disposable copy of a small Unity project first. Create one explicitly tagged scene object, one test prefab, one name-based suggestion, and one documented exemption. Exercise each operation and inspect the scene, registry JSON, Undo/Redo behavior, and build callback before enabling the package on a production project. In particular: apply a replacement, press Undo, and check the record goes back to `linked` without touching your `verified` records; delete a tag by hand and run a full scan to see the record flagged as removed; build with a deliberately unsaved build scene.

---

## 13. Troubleshooting

### The Tools → RenderGuard menu does not appear

- Wait for Unity to finish importing and compiling.
- Open **Window → General → Console** and inspect the first compile error (later errors may be consequences of the first one).
- Confirm `package.json` is directly inside `Packages/RenderGuard/`.
- Confirm the old package folder was removed before copying the new version; duplicate package folders can create duplicate assembly names or menu confusion.
- Confirm `Editor/RenderGuard.Editor.asmdef` is inside the Editor folder and its reference matches `RenderGuard.Runtime`.

### Unity says the Packages folder is read-only

Do not paste into Unity's Project window. Open the actual project directory in Windows File Explorer (Unity Hub → project menu → **Show in Explorer**), then paste the extracted `RenderGuard` folder into the file-system `Packages` directory.

### Replacement asset is rejected

- Confirm the selected asset is inside `Assets/`, not `Packages/` or an external directory.
- Confirm Unity has imported it and it can be loaded.
- A tagged GameObject needs a prefab or model asset (its category label does not matter). An asset-only record is checked against its category; see the compatibility table.
- A scene GameObject or a non-GameObject asset is rejected for tagged GameObjects.

### Apply Replacement is disabled or fails

- Automatic application only supports tagged GameObjects recorded in `.unity` scenes.
- Prefab instances and prefab asset contents are deliberately not auto-applied.
- Run **Scan Entire Project** again if the recorded object path is stale.
- Ensure the object can be uniquely located. The tag ID is the identity; if several objects share one ID, RenderGuard uses the stored path to choose and otherwise refuses to guess. Give copy/pasted tags new IDs from the Reports tab.
- Verify that the replacement prefab is still present and compatible.

### The original placeholder still appears in the build scan after linking

That is expected. Linking and verification only save a replacement path and confirm that it exists. They do not remove the `PlaceholderTag`. (The build scan uses the saved scene file, so also save the scene after applying.) Apply the replacement where supported, remove the tag manually after review, or create a documented exemption if the placeholder is intentionally allowed.

### A suggestion looks incorrect

Name/folder heuristics are advisory and can produce false positives. Use **Dismiss**. Do not approve an item based only on its confidence score.

### A prefab was changed during approval

Placeholder approval creates a timestamped backup under `Library/RenderGuardBackups/` before saving the new tag. If you need to restore the original prefab, close Unity or otherwise avoid further edits, locate the matching `.bak` file, and restore it carefully. Keep your version-control copy as the primary recovery option.

### The registry cannot be read

Check the Console for the registry parse error. RenderGuard attempts to create a timestamped `.corrupt-...bak` copy, then loads an empty in-memory registry. Do not assume the registry was repaired. Restore a known-good `registry.json` backup before doing more scans or saves if the records matter.

### Readiness score seems unexpectedly high or low

The score is a simple record-level metric. `applied` records (including asset-only records you marked as replaced), records with both the exemption flag and a non-empty exemption reason, and records removed from the project count as resolved. `linked` and `verified` records do not. Asset-only records are included in the denominator, so mark them as replaced once you have swapped the asset. A record whose tag you deleted by hand is flagged "removed from project" by the next full scan and then counts as resolved. "Applied" counts as soon as the replacement exists in the open scene, before the scene is saved. Pending suggestions are excluded. An empty registry receives 100 by definition.

### Existing tags became "Missing Script" after replacing the package

The old package folder had no `.meta` files, so Unity generated a random script GUID for `PlaceholderTag.cs` on that installation, and a new copy of the folder gets a different one. Restore the `PlaceholderTag.cs.meta` that belonged to the old installation (or revert the scenes in version control), then follow `Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md`. From 0.2.1 on the GUIDs are fixed.

### Report tab is empty

Run **Scan Entire Project** or **Scan Build Scenes**. The report is generated by a scan and is held in the current dashboard window; it is not automatically loaded from a previous report file.

---

## 14. Known limitations and out-of-scope features

RenderGuard 0.2.1 is a local Unity Editor foundation, not the complete long-term product vision.

### Detection limitations

- Heuristics can be wrong and require manual review.
- Candidate asset scanning is limited to Texture2D, Material, AudioClip and AnimationClip assets under `Assets`; prefab roots are covered by the prefab scan.
- Missing-reference inspection is limited to serialized component properties in scanned scene and prefab hierarchies (arrays over 5000 elements are skipped).
- Duplicate IDs among scene objects can be repaired from the Reports tab; copies inside prefab assets must be fixed by hand.
- A record stores one primary context path and hierarchy path; if a tag is found in several places, the record keeps one of them and a complete scan only moves it when the recorded one disappears.
- Blocking is opt-in: an object nobody tagged (or whose tag was deleted) is never blocked, only suggested.
- Registry records and suggestions are reconciled only by a *complete* project scan; a failed or partial scan changes nothing.

### Replacement limitations

- Automatic application is only for scene-authored GameObjects in scene files.
- The replacement needs to be a prefab or model asset for GameObject placeholders; the category label does not change that.
- Asset-only records are never applied to scenes automatically. For textures, audio clips and text assets, **Swap File In Place** overwrites the placeholder file (same extension required, backed up first, not undoable with Ctrl+Z); for anything else you swap the asset yourself and use Mark as Replaced.
- The operation preserves transform and child hierarchy, but does not remap all component data, external references, animations, or gameplay state.
- Prefab instances, prefab assets, and standalone asset placeholders require manual editing.
- Verification validates existence/type compatibility, not visual quality or gameplay correctness.
- Undo status refresh is best-effort and inspects currently loaded scenes.

### Build validation limitations

- Only explicit `PlaceholderTag` components in enabled Build Settings scenes block builds.
- Asset-only records, suggestions, missing references, missing scripts, and duplicate IDs are currently report-only.
- Build validation reads the saved scene files; unsaved build scenes are reported (and block, when blocking is on).
- Placeholders that exist only in prefabs, Resources or Addressables outside an enabled scene are not seen by the validator.
- Build validation does not prove that every asset dependency is production-ready.

### Not included in 0.2.1

- AI dialogue generation or generative asset creation
- Text-to-speech synthesis
- Automatic image, texture, 3D model, or animation generation
- Cloud backend or hosted dashboard
- Universal asset-reference repair
- Unreal Engine adapter
- CI/CD service integration
- Complete automated integration tests (the EditMode tests cover pure logic and the registry only)

These are possible future development areas; they should not be presented as existing 0.2.1 functionality.

---

## 15. Upgrading

**From PlaceholderGuard (any version):** follow [`Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md`](Documentation~/MIGRATING_FROM_PLACEHOLDERGUARD.md). Remove the old package folder first (the old and new packages share script GUIDs, so they must not coexist). The registry is copied automatically from `ProjectSettings/PlaceholderGuard/` to `ProjectSettings/RenderGuard/` on first load. If you came from the very first release (0.2.0, which shipped without `.meta` files), also keep your old `PlaceholderTag.cs.meta`; the guide explains how.

**Older history:** [`UPGRADE_0.2.0.md`](Documentation~/UPGRADE_0.2.0.md) and [`UPGRADE_0.2.1.md`](Documentation~/UPGRADE_0.2.1.md) were written under the old name and are kept for reference.

The registry reader accepts all earlier schemas. A registry older than schema 3 (including a v1 file with no `schemaVersion`) is backed up to `registry.json.pre-v3-backup-<timestamp>.json` before its first save in the new format. The block-builds setting moved from per-user `EditorPrefs` into the registry (default: on).

Do not leave both the old and new package folders in `Packages`.

---

## 16. Development notes

### Namespaces

- Runtime tag and enum: `RenderGuard`
- Editor systems and models: `RenderGuard.Editor`
- Tests: `RenderGuard.Editor.Tests`

### Main dependency flow

```text
Unity scene / prefab GameObjects
              │
              ▼
   Runtime/PlaceholderTag.cs
              │ explicit metadata
              ▼
Editor/Scanner/PlaceholderScanner.cs  ◄── PlaceholderHeuristics.cs
       │                 │
       │                 └── heuristic candidates
       ▼                         ▼
Editor/Registry/AssetRegistry.cs ◄── PlaceholderSuggestionService.cs
       │
       ├── PlaceholderRecord.cs (shared data models)
       │
       ├── ReplacementService.cs / PlaceholderIdService.cs
       │
       ├── PlaceholderAnalytics.cs
       │
       └── PlaceholderReportExporter.cs
                    │
                    ▼
       RenderGuardWindow.cs
                    │
                    ▼
     Developer review / Unity Editor UI

Unity build pipeline
       │
       ▼
PlaceholderBuildValidator.cs
       │
       └── scans enabled Build Settings scenes (read-only)
```

### Storage and trust boundaries

- The Unity Editor and `AssetDatabase` are the source of truth for whether an asset path resolves.
- The registry stores project-local metadata; it does not copy replacement assets.
- The scan report is a snapshot of one scan, not a continuously updated database view.
- Suggestions are untrusted candidates until a developer approves them.
- The readiness score is calculated from registry state and does not inspect the final built player.
- The build validator only reads the registry; scans for builds never write to it.

### Suggested next engineering steps

Compile and run the EditMode tests in your Unity version first. Then add automated coverage for what the current tests do not reach:

1. Scene scanning and scene open/close behavior (including the "failed scene never flags records" rule).
2. Prefab scanning and prefab backup/restore behavior.
3. Scene replacement transform/child preservation.
4. Undo and Redo status transitions.
5. Build blocking, valid/invalid exemption reasons, and the unsaved-scene check.
6. Report JSON/CSV correctness on special characters and empty fields.

---

## 17. License

RenderGuard is distributed under the Apache License, Version 2.0. See [`LICENSE`](LICENSE) for the full license text. Versions released before 0.2.1 were published under the MIT License; copies you already received under those terms stay under them.

---

**End of RenderGuard 0.2.1 documentation.**
