using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenderGuard.Editor
{
    public static class ReplacementService
    {
        public static bool ValidateReplacement(PlaceholderRecord record, string replacementPath, out string message)
        {
            message = string.Empty;
            if (record == null) { message = "Select a placeholder first."; return false; }
            if (string.IsNullOrEmpty(replacementPath) || !replacementPath.StartsWith("Assets/", StringComparison.Ordinal))
            { message = "Choose an asset inside the project's Assets folder."; return false; }

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(replacementPath);
            if (asset == null) { message = "The selected asset could not be loaded. Refresh the Project window and try again."; return false; }

            if (record.targetKind == "gameObject")
            {
                // A tagged GameObject is replaced by a prefab (or model) whatever its category label says;
                // the category (Audio, Texture, ...) only describes what kind of temporary content it stands in for.
                GameObject candidate = asset as GameObject;
                if (candidate == null)
                {
                    message = "A tagged GameObject is replaced by a prefab or model asset. The selected asset is a " + asset.GetType().Name + ".";
                    return false;
                }
                if (PrefabUtility.GetPrefabAssetType(candidate) == PrefabAssetType.NotAPrefab)
                {
                    message = "Choose a prefab asset, not a scene GameObject.";
                    return false;
                }
                return true;
            }

            string type = record.assetType ?? "Other";
            bool compatible = type == "Other" ||
                (type == "Audio" && asset is AudioClip) ||
                (type == "Texture" && asset is Texture) ||
                (type == "Material" && asset is Material) ||
                (type == "Model" && asset is GameObject) ||
                (type == "Dialogue" && (asset is TextAsset || asset is ScriptableObject)) ||
                (type == "Animation" && (asset is AnimationClip || asset is RuntimeAnimatorController)) ||
                (type == "Script" && asset is MonoScript);

            if (!compatible)
            {
                message = "The selected asset type does not match the placeholder category '" + type + "'.";
                return false;
            }
            return true;
        }

        public static bool VerifyLink(PlaceholderRecord record, out string message)
        {
            string ignored;
            if (record == null) { message = "Select a placeholder first."; return false; }

            // Follow the replacement if it was moved or renamed since it was linked.
            if (AssetRegistry.HealReplacementPath(record)) AssetRegistry.Save();

            if (!ValidateReplacement(record, record.replacementAssetPath, out ignored))
            {
                message = "Replacement link is invalid: " + ignored;
                return false;
            }
            if (record.replacementStatus != "applied")
            {
                AssetRegistry.SetReplacementStatus(record.assetId, "verified",
                    "Replacement asset exists and passed the compatibility check. This does not mean it has been applied to the scene.");
                message = "Replacement asset exists and passed the compatibility check. It is verified as a link, not applied to the scene.";
            }
            else
            {
                message = "Replacement asset link is valid. The record remains Applied; verification did not change the applied state.";
            }
            return true;
        }

        public static bool ApplyToScene(PlaceholderRecord record, out string message)
        {
            message = string.Empty;
            if (record == null) { message = "Select a placeholder first."; return false; }
            if (record.targetKind != "gameObject")
            { message = "Only tagged GameObject placeholders can be applied automatically in this release."; return false; }
            if (string.IsNullOrEmpty(record.sceneOrPrefab) || !record.sceneOrPrefab.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            { message = "Automatic application currently supports scene instances. Prefab asset records can be linked and verified, but must be edited manually."; return false; }

            if (AssetRegistry.HealReplacementPath(record)) AssetRegistry.Save();

            string validation;
            if (!ValidateReplacement(record, record.replacementAssetPath, out validation))
            { message = validation; return false; }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.replacementAssetPath);
            if (prefab == null)
            { message = "Replacement prefab could not be loaded."; return false; }

            Scene scene = SceneManager.GetSceneByPath(record.sceneOrPrefab);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try { scene = EditorSceneManager.OpenScene(record.sceneOrPrefab, OpenSceneMode.Additive); openedHere = true; }
                catch (Exception ex) { message = "Could not open target scene: " + ex.Message; return false; }
            }

            bool ambiguous;
            Transform target = PlaceholderScanner.FindTaggedTransform(scene.GetRootGameObjects(), record.assetId, record.gameObjectPath, out ambiguous);
            if (target == null)
            {
                message = ambiguous
                    ? "Several objects in the scene share this placeholder ID, so RenderGuard will not guess which one to replace. Give the copies new IDs from the Reports tab first."
                    : "Could not find the tagged object in the recorded scene. Run Scan Project and try again.";
                return false;
            }

            GameObject original = target.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(original))
            {
                message = "This object belongs to a prefab instance. To avoid replacing every instance or creating an unsafe prefab override, automatic application is limited to scene-authored GameObjects in this release. Link and verify the asset, then edit the source prefab deliberately.";
                return false;
            }
            Transform originalTransform = original.transform;
            Transform parent = originalTransform.parent;
            Vector3 localPosition = originalTransform.localPosition;
            Quaternion localRotation = originalTransform.localRotation;
            Vector3 localScale = originalTransform.localScale;
            int siblingIndex = originalTransform.GetSiblingIndex();
            string oldName = original.name;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("RenderGuard: Apply replacement");
            try
            {
                GameObject replacement = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (replacement == null) throw new InvalidOperationException("Unity could not instantiate the replacement prefab.");
                Undo.RegisterCreatedObjectUndo(replacement, "Create replacement prefab");
                if (parent != null) Undo.SetTransformParent(replacement.transform, parent, "Parent replacement prefab");
                Undo.RecordObject(replacement.transform, "Position replacement prefab");
                replacement.transform.localPosition = localPosition;
                replacement.transform.localRotation = localRotation;
                replacement.transform.localScale = localScale;
                replacement.transform.SetSiblingIndex(siblingIndex);

                // Preserve children rather than destroying their contents with the old placeholder.
                // Work from a snapshot and verify the result, so a refused reparent can never loop forever.
                var children = new List<Transform>();
                foreach (Transform child in original.transform) children.Add(child);
                foreach (Transform child in children)
                    Undo.SetTransformParent(child, replacement.transform, "Preserve placeholder child");
                if (original.transform.childCount != 0)
                    throw new InvalidOperationException("Unity refused to move " + original.transform.childCount + " child object(s) onto the replacement.");

                EditorSceneManager.MarkSceneDirty(scene);
                Undo.DestroyObjectImmediate(original);
                Undo.CollapseUndoOperations(undoGroup);

                PlaceholderRecord savedRecord = AssetRegistry.Find(record.assetId);
                if (savedRecord != null)
                {
                    savedRecord.replacementStatus = "applied";
                    savedRecord.updatedAtUtc = DateTime.UtcNow.ToString("o");
                    AssetRegistry.AddHistory(record.assetId, "applied", record.replacementAssetPath,
                        record.replacementAssetPath, record.sceneOrPrefab,
                        "Replaced scene object '" + oldName + "' with prefab '" + prefab.name + "'. Unity Undo can revert this scene operation while it remains in the Undo history.");
                    AssetRegistry.Save();
                }
                message = "Replacement instantiated in the scene. The scene is marked dirty; save it when ready, because the build uses the saved scene. " +
                          (openedHere ? "The scene was opened additively for this and stays open. " : string.Empty) +
                          "Use Edit > Undo (Ctrl+Z) to revert while this remains the latest Undo operation.";
                return true;
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                message = "Replacement was not completed; Unity Undo reverted the partial operation. " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Keeps the registry honest when the user presses Undo/Redo after an Apply.
    /// Only records that were Applied by RenderGuard are considered, and only when their scene is loaded,
    /// so unrelated Undo operations (or unloaded scenes) can never change a status.
    /// </summary>
    [InitializeOnLoad]
    internal static class ReplacementUndoWatcher
    {
        static ReplacementUndoWatcher()
        {
            Undo.undoRedoPerformed -= RefreshAppliedStatuses;
            Undo.undoRedoPerformed += RefreshAppliedStatuses;
        }

        private static Dictionary<string, HashSet<string>> CollectLoadedSceneTagIds()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (PlaceholderTag tag in root.GetComponentsInChildren<PlaceholderTag>(true))
                        if (tag != null && !string.IsNullOrEmpty(tag.AssetId)) ids.Add(tag.AssetId);
                result[scene.path] = ids;
            }
            return result;
        }

        private static void RefreshAppliedStatuses()
        {
            try
            {
                AssetRegistry.RefreshIfChangedOnDisk();
                PlaceholderRegistryData data = AssetRegistry.Data;
                if (data.placeholders.Count == 0) return;

                // Latest history action per placeholder, computed in one pass.
                var latestAction = new Dictionary<string, string>();
                foreach (PlaceholderHistoryEntry entry in data.history)
                    if (!string.IsNullOrEmpty(entry.placeholderId)) latestAction[entry.placeholderId] = entry.action;

                Dictionary<string, HashSet<string>> tagsByScene = null;
                bool changed = false;
                foreach (PlaceholderRecord record in data.placeholders)
                {
                    if (record.targetKind != "gameObject" || string.IsNullOrEmpty(record.sceneOrPrefab) ||
                        !record.sceneOrPrefab.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;

                    string last;
                    latestAction.TryGetValue(record.assetId ?? string.Empty, out last);
                    bool undoCandidate = record.replacementStatus == "applied";
                    bool redoCandidate = record.replacementStatus != "applied" && last == "undo-detected";
                    if (!undoCandidate && !redoCandidate) continue;

                    if (tagsByScene == null) tagsByScene = CollectLoadedSceneTagIds();
                    HashSet<string> ids;
                    if (!tagsByScene.TryGetValue(record.sceneOrPrefab, out ids)) continue; // scene not loaded: cannot tell
                    bool originalIsPresent = ids.Contains(record.assetId);

                    if (undoCandidate && originalIsPresent)
                    {
                        record.replacementStatus = string.IsNullOrEmpty(record.replacementAssetPath) ? "unlinked" : "linked";
                        record.updatedAtUtc = DateTime.UtcNow.ToString("o");
                        AssetRegistry.AddHistory(record.assetId, "undo-detected", record.replacementAssetPath,
                            record.replacementAssetPath, record.sceneOrPrefab, "Unity Undo restored the original tagged GameObject.");
                        changed = true;
                    }
                    else if (redoCandidate && !originalIsPresent)
                    {
                        record.replacementStatus = "applied";
                        record.updatedAtUtc = DateTime.UtcNow.ToString("o");
                        AssetRegistry.AddHistory(record.assetId, "redo-detected", record.replacementAssetPath,
                            record.replacementAssetPath, record.sceneOrPrefab, "Unity Redo reapplied the replacement operation.");
                        changed = true;
                    }
                }
                if (changed) AssetRegistry.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RenderGuard] Could not refresh replacement status after Undo/Redo: " + ex.Message);
            }
        }
    }
}
