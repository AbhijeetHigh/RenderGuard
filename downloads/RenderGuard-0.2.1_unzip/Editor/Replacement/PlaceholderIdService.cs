using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenderGuard.Editor
{
    /// <summary>Repairs copy/pasted tags that ended up sharing a placeholder ID.</summary>
    public static class PlaceholderIdService
    {
        /// <summary>
        /// Gives one scene-authored tag a fresh ID. The scene is marked dirty and never saved automatically; the change goes through Undo.
        /// Prefab assets and prefab instances are not touched.
        /// </summary>
        public static bool RegenerateId(string contextPath, string objectPath, string expectedId, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrEmpty(contextPath) || !contextPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            { message = "Only tags inside scene files can be re-identified automatically. Edit the prefab asset by hand."; return false; }

            Scene scene = SceneManager.GetSceneByPath(contextPath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try { scene = EditorSceneManager.OpenScene(contextPath, OpenSceneMode.Additive); }
                catch (Exception ex) { message = "Could not open scene: " + ex.Message; return false; }
            }

            bool ambiguous;
            Transform target = PlaceholderScanner.FindTaggedTransform(scene.GetRootGameObjects(), expectedId, objectPath, out ambiguous);
            if (target == null)
            {
                message = ambiguous
                    ? "Could not tell the copies apart by path. Rename one of them or change the ID by hand, then scan again."
                    : "Could not find the tagged object. Run a fresh scan.";
                return false;
            }
            if (PrefabUtility.IsPartOfPrefabInstance(target.gameObject))
            { message = "This tag belongs to a prefab instance and shares the prefab's ID on purpose."; return false; }

            PlaceholderTag tag = target.GetComponent<PlaceholderTag>();
            Undo.RecordObject(tag, "RenderGuard: New placeholder ID");
            tag.Configure(Guid.NewGuid().ToString("D"), tag.DisplayName, tag.AssetType, tag.Reason);
            EditorUtility.SetDirty(tag);
            EditorSceneManager.MarkSceneDirty(scene);
            message = "Gave '" + target.name + "' a new ID. Save the scene, then scan again.";
            return true;
        }
    }
}
