using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RenderGuard.Editor
{
    /// <summary>
    /// Project-window context menu: Assets > RenderGuard > Track as Placeholder.
    /// A texture or audio file cannot carry a PlaceholderTag (components attach to GameObjects only), so this registers the
    /// file directly as an asset-only record, whatever its name is.
    /// </summary>
    public static class AssetTrackingMenu
    {
        private const string MenuPath = "Assets/RenderGuard/Track as Placeholder";

        [MenuItem(MenuPath, true)]
        private static bool ValidateTrack()
        {
            return SelectedAssetPaths().Count > 0;
        }

        [MenuItem(MenuPath, false, 2000)]
        private static void Track()
        {
            List<string> paths = SelectedAssetPaths();
            int added = 0;
            int updated = 0;
            foreach (string path in paths)
            {
                string id = "asset:" + AssetDatabase.AssetPathToGUID(path);
                bool existed = AssetRegistry.Find(id) != null;
                AssetRegistry.RegisterAsset(path, Path.GetFileNameWithoutExtension(path), CategoryFor(path),
                    "Tracked manually from the Project window.", "tracked-from-project-window",
                    "Asset-only placeholder record created from the Project window. Link a replacement, then use Swap File In Place or swap it yourself and mark it replaced.");
                if (existed) updated++; else added++;
            }
            RenderGuardWindow.RepaintOpenWindows();
            string summary = added + " asset(s) now tracked" + (updated > 0 ? ", " + updated + " already tracked and refreshed" : string.Empty) +
                             ". Open Tools > RenderGuard > Open Dashboard > Placeholders to link a replacement.";
            Debug.Log("[RenderGuard] " + summary);
            EditorUtility.DisplayDialog("RenderGuard", summary, "OK");
        }

        /// <summary>Selected files inside Assets (folders and Packages content are ignored).</summary>
        public static List<string> SelectedAssetPaths()
        {
            var paths = new List<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                paths.Add(path);
            }
            return paths;
        }

        /// <summary>The category an asset is filed under, chosen from its main asset type.</summary>
        public static string CategoryFor(string assetPath)
        {
            Type type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (type == null) return "Other";
            if (typeof(Texture).IsAssignableFrom(type)) return "Texture";
            if (typeof(AudioClip).IsAssignableFrom(type)) return "Audio";
            if (typeof(Material).IsAssignableFrom(type)) return "Material";
            if (typeof(AnimationClip).IsAssignableFrom(type) || typeof(RuntimeAnimatorController).IsAssignableFrom(type)) return "Animation";
            if (typeof(MonoScript).IsAssignableFrom(type)) return "Script";
            if (typeof(GameObject).IsAssignableFrom(type)) return "Model";
            return "Other";
        }
    }
}
