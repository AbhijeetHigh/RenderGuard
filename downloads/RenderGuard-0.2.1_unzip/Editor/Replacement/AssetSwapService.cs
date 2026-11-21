using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RenderGuard.Editor
{
    /// <summary>
    /// Replaces a tracked placeholder asset (texture, audio clip or text asset) by overwriting its file with the content of
    /// the linked replacement while keeping the placeholder's .meta file. The GUID therefore stays the same, so every scene,
    /// prefab and material that used the placeholder picks up the new content without being edited.
    /// The original file is backed up first and can be restored. Unity's Undo cannot reverse a file swap; the backup does that.
    /// </summary>
    public static class AssetSwapService
    {
        public const string BackupDirectory = "Library/RenderGuardBackups";
        private const string AssetIdPrefix = "asset:";

        /// <summary>Current path of the placeholder: found through its GUID so a moved or renamed file is still located.</summary>
        public static string PlaceholderPathOf(PlaceholderRecord record)
        {
            if (record == null) return null;
            if (!string.IsNullOrEmpty(record.assetId) && record.assetId.StartsWith(AssetIdPrefix, StringComparison.Ordinal))
            {
                string byGuid = AssetDatabase.GUIDToAssetPath(record.assetId.Substring(AssetIdPrefix.Length));
                if (!string.IsNullOrEmpty(byGuid)) return byGuid;
            }
            return record.sceneOrPrefab;
        }

        /// <summary>Current path of the linked replacement (GUID first, saved path as fallback). Does not change the record.</summary>
        public static string ReplacementPathOf(PlaceholderRecord record)
        {
            if (record == null) return null;
            if (!string.IsNullOrEmpty(record.replacementAssetGuid))
            {
                string byGuid = AssetDatabase.GUIDToAssetPath(record.replacementAssetGuid);
                if (!string.IsNullOrEmpty(byGuid)) return byGuid;
            }
            return record.replacementAssetPath;
        }

        private static bool IsSupportedType(Type mainType)
        {
            // Plain single-file assets only. Models and other assets with sub-assets are excluded on purpose:
            // overwriting them would break references to their sub-assets.
            return mainType != null &&
                   (typeof(Texture).IsAssignableFrom(mainType) ||
                    typeof(AudioClip).IsAssignableFrom(mainType) ||
                    typeof(TextAsset).IsAssignableFrom(mainType));
        }

        public static bool CanSwap(PlaceholderRecord record, out string reason)
        {
            reason = string.Empty;
            if (record == null) { reason = "Select a placeholder first."; return false; }
            if (record.targetKind != "asset")
            { reason = "File swap is for asset-only records. A tagged GameObject is replaced with Apply Replacement to Scene."; return false; }
            if (string.IsNullOrEmpty(record.replacementAssetPath) && string.IsNullOrEmpty(record.replacementAssetGuid))
            { reason = "Link a replacement asset first."; return false; }

            string placeholderPath = PlaceholderPathOf(record);
            string replacementPath = ReplacementPathOf(record);
            if (string.IsNullOrEmpty(placeholderPath) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(placeholderPath)) || !File.Exists(placeholderPath))
            { reason = "The placeholder asset no longer exists."; return false; }
            if (!placeholderPath.StartsWith("Assets/", StringComparison.Ordinal))
            { reason = "Only files inside the project's Assets folder can be swapped."; return false; }
            if (string.IsNullOrEmpty(replacementPath) || !File.Exists(replacementPath))
            { reason = "The linked replacement file could not be found. Link it again."; return false; }

            string validation;
            if (!ReplacementService.ValidateReplacement(record, replacementPath, out validation))
            { reason = validation; return false; }

            if (AssetDatabase.AssetPathToGUID(placeholderPath) == AssetDatabase.AssetPathToGUID(replacementPath))
            { reason = "The replacement is the placeholder itself."; return false; }
            if (!IsSupportedType(AssetDatabase.GetMainAssetTypeAtPath(placeholderPath)))
            { reason = "File swap currently supports textures, audio clips and text assets. Swap this asset by hand, then use Mark as Replaced."; return false; }

            string placeholderExtension = Path.GetExtension(placeholderPath);
            string replacementExtension = Path.GetExtension(replacementPath);
            if (!string.Equals(placeholderExtension, replacementExtension, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The file types differ (" + placeholderExtension + " and " + replacementExtension +
                         "). A swap needs the same file type so the placeholder keeps its import settings and GUID. Export the replacement as " +
                         placeholderExtension + ", or swap it by hand.";
                return false;
            }
            return true;
        }

        public static bool CanRestore(PlaceholderRecord record)
        {
            return record != null && !string.IsNullOrEmpty(record.lastSwapBackupPath) && File.Exists(record.lastSwapBackupPath);
        }

        private static string DescribeFile(string path)
        {
            try { return path + " (" + Math.Max(1, (int)((new FileInfo(path).Length + 512) / 1024)) + " KB)"; }
            catch { return path; }
        }

        /// <summary>Text for the confirmation dialog: exactly what will be overwritten, by what, and where the backup goes.</summary>
        public static string DescribeSwap(PlaceholderRecord record)
        {
            return "The placeholder file\n  " + DescribeFile(PlaceholderPathOf(record)) +
                   "\nwill be overwritten with the content of\n  " + DescribeFile(ReplacementPathOf(record)) +
                   "\n\nThe placeholder keeps its GUID and import settings, so everything that uses it picks up the new content. " +
                   "The replacement file stays in the project (delete it later if unused). " +
                   "The original is backed up under " + BackupDirectory + " first and can be restored from the dashboard. " +
                   "Unity's Undo cannot reverse this.";
        }

        public static bool SwapInPlace(PlaceholderRecord record, out string message)
        {
            string reason;
            if (!CanSwap(record, out reason)) { message = reason; return false; }

            string placeholderPath = PlaceholderPathOf(record);
            string replacementPath = ReplacementPathOf(record);
            string backupPath = null;
            bool overwriteStarted = false;
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                backupPath = Path.Combine(BackupDirectory,
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Path.GetFileName(placeholderPath));
                File.Copy(placeholderPath, backupPath, false);

                // Version control (for example Unity VCS) may keep files read-only until they are checked out.
                bool editable = AssetDatabase.MakeEditable(placeholderPath);
                if (!editable && new FileInfo(placeholderPath).IsReadOnly)
                    throw new IOException("The placeholder file is read-only and version control did not make it editable. Check it out and try again.");

                overwriteStarted = true;
                File.Copy(replacementPath, placeholderPath, true);
                AssetDatabase.ImportAsset(placeholderPath, ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadMainAssetAtPath(placeholderPath) == null)
                    throw new InvalidOperationException("Unity could not import the file after the swap.");

                PlaceholderRecord saved = AssetRegistry.Find(record.assetId);
                if (saved != null)
                {
                    saved.lastSwapBackupPath = backupPath;
                    saved.replacementStatus = "applied";
                    saved.updatedAtUtc = DateTime.UtcNow.ToString("o");
                    AssetRegistry.AddHistory(record.assetId, "asset-swapped", placeholderPath, replacementPath, placeholderPath,
                        "Overwrote the placeholder file with the content of '" + replacementPath + "' (GUID unchanged). Original backed up to " + backupPath + ".");
                    AssetRegistry.Save();
                }
                message = "Swapped. '" + placeholderPath + "' now contains the content of '" + replacementPath +
                          "' and keeps its GUID, so existing references use the new content. The original was backed up to " + backupPath +
                          ". You can rename the file now (the GUID stays) and delete '" + replacementPath + "' if nothing else uses it.";
                return true;
            }
            catch (Exception ex)
            {
                string restoreNote = string.Empty;
                if (overwriteStarted && !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, placeholderPath, true);
                        AssetDatabase.ImportAsset(placeholderPath, ImportAssetOptions.ForceUpdate);
                        restoreNote = " The original file was restored.";
                    }
                    catch (Exception restoreException)
                    {
                        restoreNote = " Restoring the original failed (" + restoreException.Message + "); copy it back from " + backupPath + ".";
                    }
                }
                message = "The swap was not completed: " + ex.Message + restoreNote;
                return false;
            }
        }

        public static bool RestoreOriginal(PlaceholderRecord record, out string message)
        {
            if (!CanRestore(record)) { message = "There is no backup to restore for this record."; return false; }
            string placeholderPath = PlaceholderPathOf(record);
            if (string.IsNullOrEmpty(placeholderPath) || !File.Exists(placeholderPath))
            { message = "The placeholder file no longer exists, so the backup cannot be copied back over it."; return false; }
            try
            {
                bool editable = AssetDatabase.MakeEditable(placeholderPath);
                if (!editable && new FileInfo(placeholderPath).IsReadOnly)
                    throw new IOException("The placeholder file is read-only and version control did not make it editable.");
                File.Copy(record.lastSwapBackupPath, placeholderPath, true);
                AssetDatabase.ImportAsset(placeholderPath, ImportAssetOptions.ForceUpdate);

                PlaceholderRecord saved = AssetRegistry.Find(record.assetId);
                if (saved != null)
                {
                    string backup = saved.lastSwapBackupPath;
                    saved.lastSwapBackupPath = string.Empty;
                    saved.replacementStatus = string.IsNullOrEmpty(saved.replacementAssetPath) ? "unlinked" : "linked";
                    saved.updatedAtUtc = DateTime.UtcNow.ToString("o");
                    AssetRegistry.AddHistory(record.assetId, "swap-restored", backup, placeholderPath, placeholderPath,
                        "Restored the original placeholder file from its backup. The backup file was kept.");
                    AssetRegistry.Save();
                }
                message = "The original placeholder file was restored. The backup file was kept in " + BackupDirectory + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = "Could not restore the original: " + ex.Message;
                return false;
            }
        }
    }
}
