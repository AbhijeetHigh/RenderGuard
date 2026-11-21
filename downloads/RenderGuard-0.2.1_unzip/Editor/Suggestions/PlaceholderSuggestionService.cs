using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenderGuard.Editor
{
    public static class PlaceholderSuggestionService
    {
        public static bool Approve(PlaceholderSuggestion suggestion, out string message)
        {
            message = string.Empty;
            if (suggestion == null) { message = "Suggestion not found."; return false; }
            if (suggestion.targetKind == "asset")
            {
                if (AssetDatabase.LoadMainAssetAtPath(suggestion.assetPath) == null)
                { message = "The suggested asset no longer exists."; return false; }
                AssetRegistry.RegisterAsset(suggestion.assetPath, suggestion.displayName, suggestion.assetType, suggestion.reason);
                AssetRegistry.SetSuggestionStatus(suggestion.suggestionId, "approved");
                message = "Asset-only placeholder record created. It is advisory (it has no GameObject tag, so it never blocks a build). After you swap the asset yourself, use 'Mark as Replaced' so it counts toward readiness.";
                return true;
            }

            if (suggestion.contextPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return ApprovePrefabSuggestion(suggestion, out message);
            if (suggestion.contextPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return ApproveSceneSuggestion(suggestion, out message);

            message = "Unsupported suggestion target. Only scene GameObjects, prefab GameObjects and supported asset files can be approved.";
            return false;
        }

        private static bool ApproveSceneSuggestion(PlaceholderSuggestion suggestion, out string message)
        {
            Scene scene = SceneManager.GetSceneByPath(suggestion.contextPath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try { scene = EditorSceneManager.OpenScene(suggestion.contextPath, OpenSceneMode.Additive); }
                catch (Exception ex) { message = "Could not open scene: " + ex.Message; return false; }
            }
            Transform target = PlaceholderScanner.FindTransformByPath(scene.GetRootGameObjects(), suggestion.gameObjectPath);
            if (target == null) { message = "Could not find the suggested GameObject. Run a new scan."; return false; }
            if (target.GetComponent<PlaceholderTag>() != null)
            { message = "This GameObject already has a PlaceholderTag."; return false; }

            PlaceholderTag tag = Undo.AddComponent<PlaceholderTag>(target.gameObject);
            if (tag == null) { message = "Unity did not add the PlaceholderTag component."; return false; }
            tag.Configure(Guid.NewGuid().ToString("D"), target.name, PlaceholderAssetType.Other, suggestion.reason);
            EditorUtility.SetDirty(tag);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetRegistry.Register(tag, suggestion.contextPath, suggestion.gameObjectPath, false);
            AssetRegistry.SetSuggestionStatus(suggestion.suggestionId, "approved");
            AssetRegistry.Save();
            message = "PlaceholderTag added. The scene is marked dirty; save it to keep the tag.";
            return true;
        }

        private static bool ApprovePrefabSuggestion(PlaceholderSuggestion suggestion, out string message)
        {
            GameObject root = null;
            string backupPath = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(suggestion.contextPath);
                Transform target = PlaceholderScanner.FindTransformByPath(new[] { root }, suggestion.gameObjectPath);
                if (target == null) { message = "Could not find the suggested GameObject in the prefab."; return false; }
                if (target.GetComponent<PlaceholderTag>() != null)
                { message = "This prefab object already has a PlaceholderTag."; return false; }

                string backupDirectory = Path.Combine("Library", "RenderGuardBackups");
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(backupDirectory,
                    Path.GetFileName(suggestion.contextPath) + ".before-approve-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".bak");
                File.Copy(suggestion.contextPath, backupPath, false);

                PlaceholderTag tag = target.gameObject.AddComponent<PlaceholderTag>();
                tag.Configure(Guid.NewGuid().ToString("D"), target.name, PlaceholderAssetType.Other, suggestion.reason);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, suggestion.contextPath);
                if (savedPrefab == null)
                    throw new InvalidOperationException("Unity did not save the modified prefab asset.");
                AssetRegistry.Register(tag, suggestion.contextPath, suggestion.gameObjectPath, false);
                AssetRegistry.SetSuggestionStatus(suggestion.suggestionId, "approved");
                AssetRegistry.Save();
                message = "Prefab updated with a PlaceholderTag. A pre-change backup was saved under " + backupPath + ".";
                return true;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                {
                    try { File.Copy(backupPath, suggestion.contextPath, true); AssetDatabase.Refresh(); }
                    catch (Exception restoreException) { Debug.LogError("[RenderGuard] Prefab backup restore failed: " + restoreException.Message); }
                }
                // The registry may have been changed in memory before the failure; drop that and reload what is on disk.
                try { AssetRegistry.Load(); } catch { }
                message = "Could not approve prefab suggestion: " + ex.Message;
                return false;
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void Dismiss(PlaceholderSuggestion suggestion)
        {
            if (suggestion != null) AssetRegistry.SetSuggestionStatus(suggestion.suggestionId, "dismissed");
        }
    }
}
