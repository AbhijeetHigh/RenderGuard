using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RenderGuard.Editor
{
    /// <summary>
    /// Blocks builds when non-exempt PlaceholderTag components remain in enabled build scenes.
    /// The block/warn switch is a project setting stored in the registry (Tools > RenderGuard > Overview),
    /// so the whole team and the build machine share it. This validator only reads the registry; it never writes it.
    /// </summary>
    public sealed class PlaceholderBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            AssetRegistry.RefreshIfChangedOnDisk();
            bool shouldBlock = AssetRegistry.Data.blockBuilds;
            var scan = PlaceholderScanner.ScanBuildScenes();

            // An exemption only counts when the developer documents why this placeholder is allowed.
            var blocking = scan.findings.Where(f => !f.exemptFromBuildBlock ||
                string.IsNullOrWhiteSpace(f.exemptionReason)).ToList();
            var unsaved = scan.issues.Where(i => i.category == "scene-unsaved").ToList();

            if (blocking.Count == 0 && unsaved.Count == 0)
            {
                Debug.Log("[RenderGuard] Build validation passed. No blocking placeholders found in enabled build scenes; any exemptions had documented reasons.");
                return;
            }

            string message = string.Empty;
            if (blocking.Count > 0)
            {
                string summary = string.Join("\n", blocking.Select(f =>
                    "- " + f.displayName + " | " + f.sceneOrPrefab + " | " + f.gameObjectPath +
                    (f.exemptFromBuildBlock && string.IsNullOrWhiteSpace(f.exemptionReason) ? " | exemption reason required" : "")));
                message += "[RenderGuard] Found " + blocking.Count + " unresolved placeholder reference(s):\n" + summary;
            }
            if (unsaved.Count > 0)
            {
                if (message.Length > 0) message += "\n\n";
                message += "[RenderGuard] These build scenes have unsaved changes, and a build uses the saved files, so validation cannot vouch for what will ship. Save or discard the changes first:\n" +
                    string.Join("\n", unsaved.Select(i => "- " + i.contextPath));
            }

            if (shouldBlock)
                throw new BuildFailedException(message);

            Debug.LogWarning(message);
        }
    }
}
