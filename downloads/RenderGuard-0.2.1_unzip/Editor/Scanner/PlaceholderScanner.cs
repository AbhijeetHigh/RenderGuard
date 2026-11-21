using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenderGuard.Editor
{
    public static class PlaceholderScanner
    {
        /// <summary>State shared by one scan. Build-scene scans run with updateRegistry=false and never write to the registry.</summary>
        private sealed class ScanSession
        {
            public ScanReport report;
            public bool updateRegistry;
            public readonly HashSet<string> failedContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> seenSuggestionKeys = new HashSet<string>();
            public readonly Dictionary<string, List<KeyValuePair<string, string>>> occurrences =
                new Dictionary<string, List<KeyValuePair<string, string>>>();
        }

        public static ScanReport ScanProject()
        {
            AssetRegistry.Load();
            var session = new ScanSession { report = NewReport("project"), updateRegistry = true };
            var scannedScenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool completed = false;

            try
            {
                ScanLoadedScenes(session, scannedScenePaths);
                ScanAllSceneAssets(session, scannedScenePaths);
                ScanPrefabAssets(session);
                ScanCandidateAssets(session);
                completed = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // Only a scan that visited everything may decide that a tag or suggestion has gone away.
                if (completed)
                    AssetRegistry.ReconcileAfterFullScan(session.occurrences, session.failedContexts, session.seenSuggestionKeys);
                AssetRegistry.Save();
            }

            ScanReport report = session.report;
            AddDuplicateIdIssues(report);
            report.placeholderCount = report.findings.Count(f => f.findingType == "tagged");
            report.suggestions = AssetRegistry.Data.suggestions.Where(s => s.status == "pending").ToList();
            report.suggestionCount = report.suggestions.Count;
            FillIssueCounts(report);
            return report;
        }

        /// <summary>
        /// Scans the enabled Build Settings scenes. This scan is read-only with respect to the registry,
        /// so running it from a build callback never rewrites ProjectSettings.
        /// </summary>
        public static ScanReport ScanBuildScenes()
        {
            AssetRegistry.RefreshIfChangedOnDisk();
            var session = new ScanSession { report = NewReport("build-scenes"), updateRegistry = false };
            foreach (var sceneSetting in EditorBuildSettings.scenes)
            {
                if (!sceneSetting.enabled || string.IsNullOrEmpty(sceneSetting.path)) continue;
                Scene loaded = SceneManager.GetSceneByPath(sceneSetting.path);
                if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
                {
                    session.report.issues.Add(new ScanIssue
                    {
                        severity = "warning",
                        category = "scene-unsaved",
                        contextPath = sceneSetting.path,
                        message = "This build scene has unsaved changes. A build uses the saved scene file, so the scan result may not describe what will ship. Save or discard the changes first."
                    });
                }
                ScanScenePath(session, sceneSetting.path);
            }

            ScanReport report = session.report;
            AddDuplicateIdIssues(report);
            report.placeholderCount = report.findings.Count(f => f.findingType == "tagged");
            FillIssueCounts(report);
            return report;
        }

        private static void FillIssueCounts(ScanReport report)
        {
            report.missingReferenceCount = report.issues.Count(i => i.category == "missing-reference");
            report.missingScriptCount = report.issues.Count(i => i.category == "missing-script");
            report.duplicateIdCount = report.issues.Count(i => i.category == "duplicate-id");
        }

        private static ScanReport NewReport(string scope)
        {
            return new ScanReport { scannedAtUtc = DateTime.UtcNow.ToString("o"), scanScope = scope };
        }

        private static void ScanLoadedScenes(ScanSession session, HashSet<string> scannedPaths)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path) && scannedPaths.Add(scene.path))
                    ScanScene(session, scene);
            }
        }

        private static void ScanAllSceneAssets(ScanSession session, HashSet<string> scannedPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!scannedPaths.Add(path)) continue;
                EditorUtility.DisplayProgressBar("RenderGuard", "Scanning scene " + path, (float)i / Math.Max(1, guids.Length));
                ScanScenePath(session, path);
            }
        }

        private static void ScanScenePath(ScanSession session, string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                ScanScene(session, scene);
            }
            catch (Exception ex)
            {
                session.failedContexts.Add(path);
                session.report.issues.Add(new ScanIssue
                {
                    severity = "warning",
                    category = "scan-error",
                    contextPath = path,
                    message = "Could not scan scene: " + ex.Message
                });
                Debug.LogWarning("[RenderGuard] Could not scan scene " + path + ": " + ex.Message);
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ScanScene(ScanSession session, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            session.report.scenesScanned++;
            GameObject[] roots = scene.GetRootGameObjects();
            Dictionary<Transform, string> pathMap = BuildPathMap(roots);
            foreach (var root in roots)
                ScanHierarchy(session, root, scene.path, pathMap, false);
        }

        private static void ScanPrefabAssets(ScanSession session)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefabRoot = null;
                EditorUtility.DisplayProgressBar("RenderGuard", "Scanning prefab " + path, (float)i / Math.Max(1, guids.Length));
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    if (prefabRoot != null)
                    {
                        session.report.prefabsScanned++;
                        Dictionary<Transform, string> pathMap = BuildPathMap(new[] { prefabRoot });
                        ScanHierarchy(session, prefabRoot, path, pathMap, true);
                    }
                }
                catch (Exception ex)
                {
                    session.failedContexts.Add(path);
                    session.report.issues.Add(new ScanIssue
                    {
                        severity = "warning", category = "scan-error", contextPath = path,
                        message = "Could not scan prefab: " + ex.Message
                    });
                }
                finally
                {
                    if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static void ScanHierarchy(ScanSession session, GameObject root, string contextPath,
            Dictionary<Transform, string> pathMap, bool isPrefabAsset)
        {
            Transform rootTransform = root.transform;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var transform in transforms)
            {
                string objectPath;
                if (!pathMap.TryGetValue(transform, out objectPath)) objectPath = GetHierarchyPath(transform);
                InspectMissingReferences(transform.gameObject, contextPath, objectPath, session.report);

                var tag = transform.GetComponent<PlaceholderTag>();
                if (tag != null)
                {
                    string id = tag.AssetId;
                    string status = "unlinked";
                    if (session.updateRegistry)
                    {
                        // Scans do not move a record's location; that is reconciled once at the end of a full scan.
                        PlaceholderRecord record = AssetRegistry.Register(tag, contextPath, objectPath, false, false);
                        id = record.assetId;
                        status = record.replacementStatus;
                        List<KeyValuePair<string, string>> list;
                        if (!session.occurrences.TryGetValue(id, out list))
                        {
                            list = new List<KeyValuePair<string, string>>();
                            session.occurrences[id] = list;
                        }
                        list.Add(new KeyValuePair<string, string>(contextPath, objectPath));
                    }
                    else
                    {
                        PlaceholderRecord known = AssetRegistry.Find(id);
                        if (known != null) status = known.replacementStatus;
                    }

                    session.report.findings.Add(new ScanFinding
                    {
                        findingType = "tagged",
                        assetId = id,
                        displayName = string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.name : tag.DisplayName,
                        sceneOrPrefab = contextPath,
                        gameObjectPath = objectPath,
                        assetType = tag.AssetType.ToString(),
                        replacementStatus = status,
                        exemptFromBuildBlock = tag.ExemptFromBuildBlock,
                        exemptionReason = tag.ExemptionReason,
                        fromPrefabInstance = !isPrefabAsset && PrefabUtility.IsPartOfPrefabInstance(tag.gameObject),
                        message = "Explicitly tagged placeholder is present. Linking a replacement does not remove this finding."
                    });
                }
                else if (session.updateRegistry)
                {
                    // Folder hints only apply to a prefab's root object; inside a scene they would flag every object in the file.
                    bool checkFolders = isPrefabAsset && transform == rootTransform;
                    PlaceholderSuggestion suggestion = BuildGameObjectSuggestion(transform.gameObject, contextPath, objectPath, checkFolders);
                    if (suggestion != null)
                    {
                        AssetRegistry.UpsertSuggestion(suggestion);
                        session.seenSuggestionKeys.Add(suggestion.suggestionKey);
                    }
                }
            }
        }

        // ----- serialized reference inspection -------------------------------------------------------------

        private static PropertyInfo instanceIdProperty;
        private static PropertyInfo entityIdProperty;
        private static bool referenceIdLookupDone;

        private static bool HasDanglingReference(SerializedProperty property)
        {
            if (!referenceIdLookupDone)
            {
                referenceIdLookupDone = true;
                BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                instanceIdProperty = typeof(SerializedProperty).GetProperty("objectReferenceInstanceIDValue", flags);
                entityIdProperty = typeof(SerializedProperty).GetProperty("objectReferenceEntityIdValue", flags);
            }

            // Resolved through reflection so the same code compiles on Unity versions that renamed InstanceID to EntityId.
            try
            {
                if (instanceIdProperty != null)
                {
                    object value = instanceIdProperty.GetValue(property, null);
                    if (value is int) return (int)value != 0;
                }
            }
            catch { /* fall through to the EntityId variant */ }

            try
            {
                if (entityIdProperty != null)
                {
                    object value = entityIdProperty.GetValue(property, null);
                    if (value == null) return false;
                    Type type = value.GetType();
                    object none = null;
                    PropertyInfo noneProperty = type.GetProperty("None", BindingFlags.Public | BindingFlags.Static);
                    FieldInfo noneField = type.GetField("None", BindingFlags.Public | BindingFlags.Static);
                    if (noneProperty != null) none = noneProperty.GetValue(null, null);
                    else if (noneField != null) none = noneField.GetValue(null);
                    if (none == null) none = Activator.CreateInstance(type);
                    return !value.Equals(none);
                }
            }
            catch { /* treat as not dangling */ }
            return false;
        }

        private static bool ShouldEnterProperty(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ManagedReference) return true;
            if (property.propertyType != SerializedPropertyType.Generic) return false;
            if (!property.isArray) return true; // nested struct or class
            if (property.arraySize == 0 || property.arraySize > 5000) return false;
            SerializedPropertyType elementType = property.GetArrayElementAtIndex(0).propertyType;
            return elementType == SerializedPropertyType.Generic ||
                   elementType == SerializedPropertyType.ObjectReference ||
                   elementType == SerializedPropertyType.ManagedReference;
        }

        private static bool IsBookkeepingProperty(string name)
        {
            return name == "m_Script" || name == "m_CorrespondingSourceObject" || name == "m_PrefabInstance" || name == "m_PrefabAsset";
        }

        private static void InspectMissingReferences(GameObject gameObject, string contextPath,
            string objectPath, ScanReport report)
        {
            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    report.issues.Add(new ScanIssue
                    {
                        severity = "error", category = "missing-script", contextPath = contextPath,
                        objectPath = objectPath, message = "GameObject has a missing MonoBehaviour script component."
                    });
                    continue;
                }

                try
                {
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        bool enterChildren = true;
                        while (property.Next(enterChildren))
                        {
                            enterChildren = ShouldEnterProperty(property);
                            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (IsBookkeepingProperty(property.name)) continue;
                            if (property.objectReferenceValue != null) continue;
                            if (!HasDanglingReference(property)) continue;
                            report.issues.Add(new ScanIssue
                            {
                                severity = "error", category = "missing-reference", contextPath = contextPath,
                                objectPath = objectPath, propertyPath = property.propertyPath,
                                message = component.GetType().Name + " has a missing object reference at " + property.propertyPath + "."
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.issues.Add(new ScanIssue
                    {
                        severity = "warning", category = "inspection-warning", contextPath = contextPath,
                        objectPath = objectPath, message = "Could not inspect " + component.GetType().Name + ": " + ex.Message
                    });
                }
            }
        }

        // ----- suggestions ---------------------------------------------------------------------------------

        private static PlaceholderSuggestion BuildGameObjectSuggestion(GameObject gameObject, string contextPath,
            string objectPath, bool checkFolders)
        {
            string reason;
            float confidence;
            if (!PlaceholderHeuristics.Evaluate(gameObject.name, contextPath, checkFolders, out confidence, out reason)) return null;
            string key = "go|" + contextPath + "|" + objectPath;
            return new PlaceholderSuggestion
            {
                suggestionId = StableId(key), suggestionKey = key, targetKind = "gameObject",
                contextPath = contextPath, gameObjectPath = objectPath, displayName = gameObject.name,
                assetType = "Other", confidence = confidence, reason = reason
            };
        }

        private static void ScanCandidateAssets(ScanSession session)
        {
            // Prefabs are not listed here: their root object is already suggested (and can actually be tagged) by the prefab scan.
            string[][] filters =
            {
                new[] { "t:Texture2D", "Texture" },
                new[] { "t:Material", "Material" },
                new[] { "t:AudioClip", "Audio" },
                new[] { "t:AnimationClip", "Animation" }
            };
            foreach (string[] filter in filters)
            {
                string[] guids = AssetDatabase.FindAssets(filter[0], new[] { "Assets" });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    session.report.assetsChecked++;
                    string reason;
                    float confidence;
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!PlaceholderHeuristics.Evaluate(fileName, path, true, out confidence, out reason)) continue;
                    string key = "asset|" + path;
                    AssetRegistry.UpsertSuggestion(new PlaceholderSuggestion
                    {
                        suggestionId = StableId(key), suggestionKey = key, targetKind = "asset",
                        assetPath = path, contextPath = path, displayName = fileName,
                        assetType = filter[1], confidence = confidence, reason = reason
                    });
                    session.seenSuggestionKeys.Add(key);
                }
            }
        }

        // ----- duplicate IDs -------------------------------------------------------------------------------

        private static void AddDuplicateIdIssues(ScanReport report)
        {
            var groups = report.findings.Where(f => f.findingType == "tagged" && !string.IsNullOrEmpty(f.assetId))
                .GroupBy(f => f.assetId).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                List<ScanFinding> items = group.ToList();
                // Instances of a prefab share the prefab's tag ID by design. Two independently authored objects
                // (scene objects or prefab assets) sharing one ID are copies, and would be merged into one record.
                List<ScanFinding> authored = items.Where(f => !f.fromPrefabInstance).ToList();
                if (authored.Count < 2) continue;

                var lines = new StringBuilder();
                foreach (ScanFinding item in authored.Take(6))
                    lines.Append("\n  - ").Append(item.sceneOrPrefab).Append(" :: ").Append(item.gameObjectPath);
                if (authored.Count > 6) lines.Append("\n  - ... and ").Append(authored.Count - 6).Append(" more");

                report.issues.Add(new ScanIssue
                {
                    severity = "warning", category = "duplicate-id", assetId = group.Key,
                    contextPath = authored[0].sceneOrPrefab, objectPath = authored[0].gameObjectPath,
                    message = "Placeholder ID " + group.Key + " is used by " + authored.Count +
                              " independently authored objects (likely a copy/paste). They are merged into one registry record. Give the extra copies new IDs from the Reports tab." +
                              lines
                });
            }
        }

        // ----- hierarchy paths -----------------------------------------------------------------------------

        private static string Segment(string name, int ordinal)
        {
            string safe = (name ?? string.Empty).Replace("/", "%2F");
            return ordinal >= 0 ? safe + "[" + ordinal + "]" : safe;
        }

        /// <summary>
        /// Slash-separated path from the root. Siblings that share a name get a [n] suffix, so every path is unique
        /// and "/" inside a name is escaped as %2F.
        /// </summary>
        public static string GetHierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            while (transform != null)
            {
                parts.Push(SegmentFor(transform));
                transform = transform.parent;
            }
            return string.Join("/", parts.ToArray());
        }

        private static string SegmentFor(Transform t)
        {
            Transform parent = t.parent;
            int count = 0;
            int ordinal = 0;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (sibling.name != t.name) continue;
                    if (sibling == t) ordinal = count;
                    count++;
                }
            }
            else
            {
                Scene scene = t.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded)
                {
                    foreach (GameObject sibling in scene.GetRootGameObjects())
                    {
                        if (sibling.name != t.name) continue;
                        if (sibling.transform == t) ordinal = count;
                        count++;
                    }
                }
                else count = 1;
            }
            return Segment(t.name, count > 1 ? ordinal : -1);
        }

        /// <summary>Computes the path of every transform under the given roots in one pass (linear time).</summary>
        public static Dictionary<Transform, string> BuildPathMap(IList<GameObject> roots)
        {
            var map = new Dictionary<Transform, string>();
            if (roots == null) return map;
            var counts = new Dictionary<string, int>();
            foreach (GameObject root in roots)
            {
                int c;
                counts[root.name] = counts.TryGetValue(root.name, out c) ? c + 1 : 1;
            }
            var seen = new Dictionary<string, int>();
            foreach (GameObject root in roots)
            {
                int ordinal;
                seen.TryGetValue(root.name, out ordinal);
                seen[root.name] = ordinal + 1;
                WalkPaths(root.transform, Segment(root.name, counts[root.name] > 1 ? ordinal : -1), map);
            }
            return map;
        }

        private static void WalkPaths(Transform transform, string path, Dictionary<Transform, string> map)
        {
            map[transform] = path;
            int childCount = transform.childCount;
            if (childCount == 0) return;
            var counts = new Dictionary<string, int>(childCount);
            for (int i = 0; i < childCount; i++)
            {
                string childName = transform.GetChild(i).name;
                int c;
                counts[childName] = counts.TryGetValue(childName, out c) ? c + 1 : 1;
            }
            var seen = new Dictionary<string, int>(childCount);
            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);
                int ordinal;
                seen.TryGetValue(child.name, out ordinal);
                seen[child.name] = ordinal + 1;
                WalkPaths(child, path + "/" + Segment(child.name, counts[child.name] > 1 ? ordinal : -1), map);
            }
        }

        public static Transform FindTransformByPath(IList<GameObject> roots, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (KeyValuePair<Transform, string> pair in BuildPathMap(roots))
                if (pair.Value == path) return pair.Key;
            return null;
        }

        /// <summary>
        /// Finds the object carrying a tag with the given ID. The ID is the identity; the path only breaks ties
        /// when several objects share the ID. Returns null (and ambiguous=true) rather than guessing.
        /// </summary>
        public static Transform FindTaggedTransform(IList<GameObject> roots, string assetId, string pathHint, out bool ambiguous)
        {
            ambiguous = false;
            if (roots == null || string.IsNullOrEmpty(assetId)) return null;
            var candidates = new List<Transform>();
            foreach (GameObject root in roots)
                foreach (PlaceholderTag tag in root.GetComponentsInChildren<PlaceholderTag>(true))
                    if (tag != null && tag.AssetId == assetId) candidates.Add(tag.transform);
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            Dictionary<Transform, string> map = BuildPathMap(roots);
            Transform match = null;
            foreach (Transform candidate in candidates)
            {
                string path;
                if (!map.TryGetValue(candidate, out path) || path != pathHint) continue;
                if (match != null) { ambiguous = true; return null; }
                match = candidate;
            }
            if (match == null) ambiguous = true;
            return match;
        }

        public static string StableId(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return new Guid(bytes).ToString("N");
            }
        }
    }
}
