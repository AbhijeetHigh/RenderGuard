using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RenderGuard.Editor
{
    public static class AssetRegistry
    {
        public const string RegistryDirectory = "ProjectSettings/RenderGuard";
        public const string RegistryPath = RegistryDirectory + "/registry.json";
        // Where the registry lived when this package was still called PlaceholderGuard. It is copied (never moved) on first load.
        public const string LegacyRegistryPath = "ProjectSettings/PlaceholderGuard/registry.json";

        private static PlaceholderRegistryData cached;
        private static bool needsMigrationBackup;
        private static DateTime loadedWriteTimeUtc;
        private static string lastSavedJson;
        private static string pathOverride;
        private static string legacyPathOverride;

        /// <summary>Increments whenever the in-memory registry is reloaded or a changed registry is saved. UIs use it to refresh cached views.</summary>
        public static int ChangeCounter { get; private set; }

        public static string CurrentRegistryPath
        {
            get { return string.IsNullOrEmpty(pathOverride) ? RegistryPath : pathOverride; }
        }

        private static string CurrentLegacyPath
        {
            get { return string.IsNullOrEmpty(pathOverride) ? LegacyRegistryPath : legacyPathOverride; }
        }

        public static PlaceholderRegistryData Data
        {
            get
            {
                if (cached == null) Load();
                return cached;
            }
        }

        /// <summary>For tests: redirect the registry to another file so the real project registry is never touched. Pass null to restore.</summary>
        public static void UseRegistryPathForTests(string path, string legacyPath = null)
        {
            pathOverride = path;
            legacyPathOverride = legacyPath;
            cached = null;
            lastSavedJson = null;
            needsMigrationBackup = false;
            ChangeCounter++;
        }

        private static PlaceholderRegistryData NewData()
        {
            return new PlaceholderRegistryData { schemaVersion = PlaceholderRegistryData.CurrentSchemaVersion };
        }

        /// <summary>Reloads from disk only when the file was changed by something else (version control, another editor session).</summary>
        public static void RefreshIfChangedOnDisk()
        {
            if (cached == null) { Load(); return; }
            string path = CurrentRegistryPath;
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (stamp != loadedWriteTimeUtc) Load();
        }

        /// <summary>
        /// Projects that used the package under its former name keep their registry in ProjectSettings/PlaceholderGuard.
        /// If the new registry does not exist yet, that file is copied over once. The old file is left untouched.
        /// </summary>
        private static void MigrateLegacyRegistryIfNeeded()
        {
            string path = CurrentRegistryPath;
            string legacy = CurrentLegacyPath;
            if (string.IsNullOrEmpty(legacy) || File.Exists(path) || !File.Exists(legacy)) return;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(legacy, path, false);
                Debug.Log("[RenderGuard] Copied the registry from its former PlaceholderGuard location (" + legacy +
                          ") to " + path + ". The old file was left in place; delete it once you have checked the dashboard.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RenderGuard] Could not copy the registry from its former PlaceholderGuard location: " + ex.Message);
            }
        }

        public static void Load()
        {
            ChangeCounter++;
            MigrateLegacyRegistryIfNeeded();
            string path = CurrentRegistryPath;
            lastSavedJson = null;
            if (!File.Exists(path))
            {
                needsMigrationBackup = false;
                loadedWriteTimeUtc = DateTime.MinValue;
                cached = NewData();
                EnsureCollections();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                cached = JsonUtility.FromJson<PlaceholderRegistryData>(json) ?? NewData();
                // schemaVersion defaults to 0, so a v1 file that never wrote the field is detected as older than current.
                needsMigrationBackup = cached.schemaVersion < PlaceholderRegistryData.CurrentSchemaVersion;
                EnsureCollections();
                cached.schemaVersion = PlaceholderRegistryData.CurrentSchemaVersion;
                loadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                if (!needsMigrationBackup) lastSavedJson = JsonUtility.ToJson(cached, true);
            }
            catch (Exception ex)
            {
                string backupPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak";
                try { File.Copy(path, backupPath, false); }
                catch (Exception backupException)
                {
                    Debug.LogWarning("[RenderGuard] Could not back up the malformed registry: " + backupException.Message);
                }
                Debug.LogError("[RenderGuard] Could not read registry. A backup was attempted at " + backupPath + ". " + ex.Message);
                needsMigrationBackup = false;
                loadedWriteTimeUtc = DateTime.MinValue;
                cached = NewData();
                EnsureCollections();
            }
        }

        private static void EnsureCollections()
        {
            if (cached == null) cached = NewData();
            if (cached.placeholders == null) cached.placeholders = new List<PlaceholderRecord>();
            if (cached.suggestions == null) cached.suggestions = new List<PlaceholderSuggestion>();
            if (cached.history == null) cached.history = new List<PlaceholderHistoryEntry>();
            foreach (var record in cached.placeholders)
            {
                if (string.IsNullOrEmpty(record.targetKind)) record.targetKind = "gameObject";
                if (string.IsNullOrEmpty(record.replacementStatus)) record.replacementStatus = "unlinked";
            }
        }

        public static void Save()
        {
            EnsureCollections();
            cached.schemaVersion = PlaceholderRegistryData.CurrentSchemaVersion;
            string path = CurrentRegistryPath;
            string json = JsonUtility.ToJson(cached, true);

            // Nothing changed since the last load/save: do not touch the file, so version control sees no diff.
            if (!needsMigrationBackup && json == lastSavedJson && File.Exists(path)) return;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (needsMigrationBackup && File.Exists(path))
            {
                string backupPath = path + ".pre-v" + PlaceholderRegistryData.CurrentSchemaVersion +
                    "-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Copy(path, backupPath, false);
                needsMigrationBackup = false;
            }
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                try { File.Replace(tempPath, path, null); }
                catch
                {
                    File.Delete(path);
                    File.Move(tempPath, path);
                }
            }
            else File.Move(tempPath, path);

            lastSavedJson = json;
            loadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            ChangeCounter++;
        }

        public static void SetBlockBuilds(bool value)
        {
            if (Data.blockBuilds == value) return;
            Data.blockBuilds = value;
            Save();
        }

        // ----- helpers -------------------------------------------------------------------------------------

        private static bool Assign(ref string field, string value)
        {
            if (string.Equals(field ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal)) return false;
            field = value;
            return true;
        }

        private static bool Assign(ref bool field, bool value)
        {
            if (field == value) return false;
            field = value;
            return true;
        }

        private static bool IsScenePath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContextExists(string path)
        {
            return !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        private static string Now() { return DateTime.UtcNow.ToString("o"); }

        // ----- registration --------------------------------------------------------------------------------

        /// <summary>
        /// Creates or refreshes the record for a tag. updateLocation=false is used by scans, which reconcile the recorded
        /// location once at the end (so several instances sharing an ID cannot make the location flip-flop between scans).
        /// A new record always receives the location it was first seen at.
        /// </summary>
        public static PlaceholderRecord Register(PlaceholderTag tag, string contextPath, string objectPath,
            bool saveImmediately = true, bool updateLocation = true)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            string id = tag.AssetId;
            if (string.IsNullOrWhiteSpace(id))
            {
                // OnValidate normally assigns the ID as soon as the tag is added. This is only a safety net.
                id = Guid.NewGuid().ToString("D");
                tag.Configure(id, tag.DisplayName, tag.AssetType, tag.Reason);
                EditorUtility.SetDirty(tag);
            }

            bool changed = false;
            var record = Find(id);
            bool isNew = record == null;
            if (isNew)
            {
                record = new PlaceholderRecord
                {
                    assetId = id,
                    createdAtUtc = Now(),
                    replacementStatus = "unlinked"
                };
                Data.placeholders.Add(record);
                changed = true;
            }

            changed |= Assign(ref record.displayName, string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.gameObject.name : tag.DisplayName);
            changed |= Assign(ref record.assetType, tag.AssetType.ToString());
            changed |= Assign(ref record.targetKind, "gameObject");
            changed |= Assign(ref record.reason, tag.Reason);
            changed |= Assign(ref record.exemptFromBuildBlock, tag.ExemptFromBuildBlock);
            changed |= Assign(ref record.exemptionReason, tag.ExemptionReason);

            bool sameRecordedContext = string.Equals(record.sceneOrPrefab, contextPath, StringComparison.OrdinalIgnoreCase);

            if (isNew || updateLocation)
            {
                // Keep the first discovered context, but prefer a scene over a prefab asset (scene replacement is actionable),
                // and move on if the recorded context no longer exists or the record was flagged missing.
                bool existingIsScene = IsScenePath(record.sceneOrPrefab);
                bool incomingIsScene = IsScenePath(contextPath);
                bool adopt = string.IsNullOrEmpty(record.sceneOrPrefab) || sameRecordedContext ||
                    (!existingIsScene && incomingIsScene) || record.missingFromProject || !ContextExists(record.sceneOrPrefab);
                if (adopt)
                {
                    changed |= Assign(ref record.sceneOrPrefab, contextPath);
                    changed |= Assign(ref record.gameObjectPath, objectPath);
                }
            }

            if (record.missingFromProject)
            {
                record.missingFromProject = false;
                record.missingSinceUtc = string.Empty;
                AddHistory(id, "tag-reappeared", null, null, contextPath, "A scan found the PlaceholderTag again.");
                changed = true;
            }

            if (sameRecordedContext && record.replacementStatus == "applied")
            {
                record.replacementStatus = string.IsNullOrEmpty(record.replacementAssetPath) ? "unlinked" : "linked";
                AddHistory(id, "tag-resurfaced", record.replacementAssetPath, record.replacementAssetPath,
                    contextPath, "A fresh scan found the original PlaceholderTag again. The previous scene replacement may have been undone or the scene may not have been saved.");
                changed = true;
            }

            if (changed) record.updatedAtUtc = Now();
            if (saveImmediately) Save();
            return record;
        }

        public static PlaceholderRecord RegisterAsset(string assetPath, string displayName, string assetType, string reason,
            string historyAction = "approved-asset-suggestion", string historyDetails = null)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("Not a known project asset: " + assetPath);
            string id = "asset:" + guid;
            var record = Find(id);
            if (record == null)
            {
                record = new PlaceholderRecord
                {
                    assetId = id,
                    createdAtUtc = Now(),
                    replacementStatus = "unlinked"
                };
                Data.placeholders.Add(record);
            }
            record.displayName = displayName;
            record.assetType = assetType;
            record.targetKind = "asset";
            record.sceneOrPrefab = assetPath;
            record.gameObjectPath = string.Empty;
            record.reason = reason;
            record.missingFromProject = false;
            record.missingSinceUtc = string.Empty;
            record.updatedAtUtc = Now();
            AddHistory(id, historyAction, null, null, assetPath,
                historyDetails ?? "An asset-only placeholder record was created. Build-scene enforcement applies to tagged GameObjects only; use Swap File In Place, or swap the asset yourself and mark the record as replaced.");
            Save();
            return record;
        }

        // ----- replacement links ---------------------------------------------------------------------------

        public static void LinkReplacement(string assetId, string replacementAssetPath)
        {
            var record = Find(assetId);
            if (record == null) throw new InvalidOperationException("Placeholder ID not found: " + assetId);
            if (string.IsNullOrWhiteSpace(replacementAssetPath))
                throw new ArgumentException("Choose a replacement asset before linking.");
            if (!replacementAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Replacement must be inside the Unity Assets folder.");
            if (AssetDatabase.LoadMainAssetAtPath(replacementAssetPath) == null)
                throw new FileNotFoundException("The selected replacement asset could not be found or loaded.", replacementAssetPath);

            string previous = record.replacementAssetPath;
            record.replacementAssetPath = replacementAssetPath;
            record.replacementAssetGuid = AssetDatabase.AssetPathToGUID(replacementAssetPath);
            // An already-applied record stays applied: re-linking does not undo what happened in the scene.
            if (record.replacementStatus != "applied") record.replacementStatus = "linked";
            record.updatedAtUtc = Now();
            AddHistory(assetId, "linked", previous, replacementAssetPath, record.sceneOrPrefab,
                "Replacement asset path saved. The placeholder has not been replaced.");
            Save();
        }

        public static void UnlinkReplacement(string assetId)
        {
            var record = Find(assetId);
            if (record == null) throw new InvalidOperationException("Placeholder ID not found: " + assetId);
            string previous = record.replacementAssetPath;
            record.replacementAssetPath = string.Empty;
            record.replacementAssetGuid = string.Empty;
            if (record.replacementStatus != "applied") record.replacementStatus = "unlinked";
            record.updatedAtUtc = Now();
            AddHistory(assetId, "unlinked", previous, null, record.sceneOrPrefab, "Replacement link removed.");
            Save();
        }

        /// <summary>If the linked replacement was moved or renamed, follows it via its GUID. Returns true when the path changed.</summary>
        public static bool HealReplacementPath(PlaceholderRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.replacementAssetGuid)) return false;
            string current = AssetDatabase.GUIDToAssetPath(record.replacementAssetGuid);
            if (string.IsNullOrEmpty(current) || string.Equals(current, record.replacementAssetPath, StringComparison.Ordinal)) return false;
            string previous = record.replacementAssetPath;
            record.replacementAssetPath = current;
            record.updatedAtUtc = Now();
            AddHistory(record.assetId, "replacement-moved", previous, current, record.sceneOrPrefab,
                "The linked replacement asset was moved or renamed; the saved path was updated from its GUID.");
            return true;
        }

        public static void SetReplacementStatus(string assetId, string status, string details)
        {
            var record = Find(assetId);
            if (record == null) throw new InvalidOperationException("Placeholder ID not found: " + assetId);
            string previous = record.replacementStatus;
            record.replacementStatus = status;
            record.updatedAtUtc = Now();
            AddHistory(assetId, "status:" + status, record.replacementAssetPath, record.replacementAssetPath,
                record.sceneOrPrefab, (details ?? "") + " Previous status: " + previous);
            Save();
        }

        /// <summary>
        /// For records RenderGuard cannot apply itself (asset-only records): the developer confirms the swap was done by hand.
        /// </summary>
        public static void MarkResolved(string assetId, string details)
        {
            var record = Find(assetId);
            if (record == null) throw new InvalidOperationException("Placeholder ID not found: " + assetId);
            SetReplacementStatus(assetId, "applied", details);
        }

        public static void RemoveRecord(string assetId)
        {
            var record = Find(assetId);
            if (record == null) return;
            Data.placeholders.Remove(record);
            AddHistory(assetId, "record-removed", record.replacementAssetPath, null, record.sceneOrPrefab,
                "Record '" + record.displayName + "' was removed from the registry by the user.");
            Save();
        }

        public static void AddHistory(string assetId, string action, string previousPath, string newPath,
            string contextPath, string details)
        {
            Data.history.Add(new PlaceholderHistoryEntry
            {
                occurredAtUtc = Now(),
                placeholderId = assetId,
                action = action,
                previousAssetPath = previousPath,
                newAssetPath = newPath,
                contextPath = contextPath,
                details = details
            });
        }

        // ----- suggestions ---------------------------------------------------------------------------------

        public static void UpsertSuggestion(PlaceholderSuggestion incoming)
        {
            var existing = Data.suggestions.FirstOrDefault(s => s.suggestionKey == incoming.suggestionKey);
            if (existing == null)
            {
                incoming.firstSeenUtc = Now();
                incoming.lastSeenUtc = incoming.firstSeenUtc;
                incoming.status = "pending";
                Data.suggestions.Add(incoming);
                return;
            }

            // lastSeenUtc only moves when the suggestion's content changes, so unchanged rescans leave the registry byte-identical.
            bool changed = false;
            changed |= Assign(ref existing.displayName, incoming.displayName);
            changed |= Assign(ref existing.assetType, incoming.assetType);
            changed |= Assign(ref existing.reason, incoming.reason);
            changed |= Assign(ref existing.contextPath, incoming.contextPath);
            changed |= Assign(ref existing.gameObjectPath, incoming.gameObjectPath);
            changed |= Assign(ref existing.assetPath, incoming.assetPath);
            changed |= Assign(ref existing.targetKind, incoming.targetKind);
            if (Math.Abs(existing.confidence - incoming.confidence) > 0.0001f) { existing.confidence = incoming.confidence; changed = true; }
            if (changed) existing.lastSeenUtc = Now();
            // Approved/dismissed decisions are preserved across scans.
        }

        public static void SetSuggestionStatus(string suggestionId, string status)
        {
            var suggestion = Data.suggestions.FirstOrDefault(s => s.suggestionId == suggestionId);
            if (suggestion == null) throw new InvalidOperationException("Suggestion not found: " + suggestionId);
            suggestion.status = status;
            Save();
        }

        public static PlaceholderRecord Find(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return null;
            return Data.placeholders.FirstOrDefault(p => p.assetId == assetId);
        }

        // ----- full-scan reconciliation --------------------------------------------------------------------

        /// <summary>
        /// Called once after a complete project scan. occurrences maps placeholder ID to every (context, objectPath) where its tag was seen.
        /// Records whose tag vanished are flagged (not deleted), moved records follow their tag, moved assets follow their GUID, and
        /// pending suggestions that were not seen again are dropped. Contexts that failed to scan are never used to flag anything.
        /// </summary>
        public static void ReconcileAfterFullScan(Dictionary<string, List<KeyValuePair<string, string>>> occurrences,
            HashSet<string> failedContexts, HashSet<string> seenSuggestionKeys)
        {
            foreach (PlaceholderRecord record in Data.placeholders)
            {
                HealReplacementPath(record);

                if (record.targetKind == "asset")
                {
                    ReconcileAssetRecord(record);
                    continue;
                }

                List<KeyValuePair<string, string>> seen;
                if (!string.IsNullOrEmpty(record.assetId) && occurrences.TryGetValue(record.assetId, out seen) && seen.Count > 0)
                {
                    bool stillThere = seen.Any(o => string.Equals(o.Key, record.sceneOrPrefab, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(o.Value, record.gameObjectPath, StringComparison.Ordinal));
                    if (!stillThere)
                    {
                        KeyValuePair<string, string> pick = seen.FirstOrDefault(o => string.Equals(o.Key, record.sceneOrPrefab, StringComparison.OrdinalIgnoreCase));
                        if (pick.Key == null) pick = seen.FirstOrDefault(o => IsScenePath(o.Key));
                        if (pick.Key == null) pick = seen[0];
                        string previous = record.sceneOrPrefab;
                        record.sceneOrPrefab = pick.Key;
                        record.gameObjectPath = pick.Value;
                        record.updatedAtUtc = Now();
                        AddHistory(record.assetId, "relocated", null, null, pick.Key,
                            "The tag was found at a different location than recorded (previously '" + previous + "').");
                    }
                    continue;
                }

                // Not found anywhere. Only flag it if the whole recorded context was really scanned.
                bool contextInScope = !string.IsNullOrEmpty(record.sceneOrPrefab) &&
                    record.sceneOrPrefab.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
                bool contextFailed = failedContexts != null && failedContexts.Contains(record.sceneOrPrefab ?? string.Empty);
                if (contextInScope && !contextFailed && record.replacementStatus != "applied" && !record.missingFromProject)
                    MarkMissing(record, "The PlaceholderTag was not found in any scanned scene or prefab. It was probably removed or replaced by hand.");
            }

            if (seenSuggestionKeys != null)
            {
                Data.suggestions.RemoveAll(s => s.status == "pending" && !string.IsNullOrEmpty(s.suggestionKey) &&
                    !seenSuggestionKeys.Contains(s.suggestionKey) &&
                    !(failedContexts != null && failedContexts.Contains(s.contextPath ?? string.Empty)));
            }
        }

        private static void ReconcileAssetRecord(PlaceholderRecord record)
        {
            const string prefix = "asset:";
            if (string.IsNullOrEmpty(record.assetId) || !record.assetId.StartsWith(prefix, StringComparison.Ordinal)) return;
            string guid = record.assetId.Substring(prefix.Length);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                if (record.replacementStatus != "applied" && !record.missingFromProject)
                    MarkMissing(record, "The placeholder asset no longer exists in the project.");
                return;
            }
            if (record.missingFromProject)
            {
                record.missingFromProject = false;
                record.missingSinceUtc = string.Empty;
                record.updatedAtUtc = Now();
                AddHistory(record.assetId, "asset-reappeared", null, null, path, "The placeholder asset exists again.");
            }
            if (!string.Equals(record.sceneOrPrefab, path, StringComparison.Ordinal))
            {
                string previous = record.sceneOrPrefab;
                record.sceneOrPrefab = path;
                record.updatedAtUtc = Now();
                AddHistory(record.assetId, "asset-moved", previous, path, path, "The placeholder asset was moved or renamed; the saved path was updated from its GUID.");
            }
        }

        private static void MarkMissing(PlaceholderRecord record, string details)
        {
            record.missingFromProject = true;
            record.missingSinceUtc = Now();
            record.updatedAtUtc = record.missingSinceUtc;
            AddHistory(record.assetId, "tag-missing", record.replacementAssetPath, record.replacementAssetPath, record.sceneOrPrefab, details);
        }
    }
}
