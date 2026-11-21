using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RenderGuard.Editor
{
    public sealed class RenderGuardWindow : EditorWindow
    {
        private const int SuggestionPageSize = 20;

        private ScanReport lastReport;
        private Vector2 scroll;
        private Vector2 historyScroll;
        private int selectedTab;
        private string selectedAssetId;
        private string searchText = string.Empty;
        private UnityEngine.Object replacementSelection;
        private string statusMessage;
        private MessageType statusMessageType = MessageType.Info;

        // Cached, filtered views of the registry. They are rebuilt only when the registry or the filter changes.
        private List<PlaceholderRecord> visibleRecords = new List<PlaceholderRecord>();
        private int visibleRecordsVersion = -1;
        private string visibleRecordsSearch;
        private List<PlaceholderSuggestion> visibleSuggestions = new List<PlaceholderSuggestion>();
        private int visibleSuggestionsVersion = -1;
        private float visibleSuggestionsMinConfidence = -1f;
        private float minConfidence;
        private int suggestionPage;

        private readonly string[] tabNames = { "Overview", "Placeholders", "Suggestions", "Reports" };

        // Cached result of the (AssetDatabase-heavy) swap check, so it is not recomputed on every repaint.
        private string swapCheckKey;
        private bool swapCheckOk;
        private string swapCheckReason = string.Empty;

        /// <summary>Repaints every open dashboard (used after registry changes made outside the window).</summary>
        public static void RepaintOpenWindows()
        {
            foreach (RenderGuardWindow window in Resources.FindObjectsOfTypeAll<RenderGuardWindow>())
                if (window != null) window.Repaint();
        }

        [MenuItem("Tools/RenderGuard/Open Dashboard")]
        public static void Open()
        {
            GetWindow<RenderGuardWindow>("RenderGuard");
        }

        private void OnEnable()
        {
            AssetRegistry.RefreshIfChangedOnDisk();
        }

        private void OnFocus()
        {
            AssetRegistry.RefreshIfChangedOnDisk();
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            EditorGUILayout.Space(8);
            switch (selectedTab)
            {
                case 0: DrawOverview(); break;
                case 1: DrawPlaceholders(); break;
                case 2: DrawSuggestions(); break;
                case 3: DrawReports(); break;
            }
        }

        /// <summary>
        /// Runs an action on the next editor tick instead of inside OnGUI. Scene opening, scans and modal dialogs
        /// do not belong inside an IMGUI layout pass and can otherwise throw layout exceptions.
        /// </summary>
        private void Defer(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try { action(); }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    if (this != null) SetMessage("Action failed: " + ex.Message, MessageType.Error);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    if (this != null) Repaint();
                }
            };
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("RenderGuard", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Track temporary assets, manage replacements and review project readiness.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan Entire Project", GUILayout.Height(28))) Defer(RunProjectScan);
                if (GUILayout.Button("Scan Build Scenes", GUILayout.Height(28))) Defer(RunBuildScan);
            }
            if (!string.IsNullOrEmpty(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, statusMessageType);
            EditorGUILayout.Space(3);
        }

        // ----- Overview ------------------------------------------------------------------------------------

        private void DrawOverview()
        {
            var data = AssetRegistry.Data;
            var summary = PlaceholderAnalytics.BuildSummary(data);
            EditorGUILayout.LabelField("Project readiness", EditorStyles.boldLabel);
            Rect scoreRect = GUILayoutUtility.GetRect(1, 24, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(scoreRect, summary.readinessScore / 100f, "Readiness estimate: " + summary.readinessScore + "%");
            EditorGUILayout.HelpBox("Score formula: records that are applied (or marked replaced), explicitly exempt with a documented reason, or removed from the project count as resolved. Linked or verified links alone do not count, because the placeholder may still remain in the scene. Automatic suggestions do not affect this score. 'Applied' counts as soon as the replacement exists in the open scene; save the scene to make it permanent.", MessageType.Info);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawMetric("Registered placeholders", summary.totalTracked.ToString());
                DrawMetric("Unlinked", summary.unlinked.ToString());
                DrawMetric("Linked", summary.linked.ToString());
                DrawMetric("Verified links", summary.verified.ToString());
                DrawMetric("Applied replacements", summary.applied.ToString());
                DrawMetric("Explicitly exempt", summary.exempt.ToString());
                DrawMetric("Removed from project", summary.removed.ToString());
                DrawMetric("Pending suggestions", summary.pendingSuggestions.ToString());
                DrawMetric("Asset-only records", summary.assetOnlyRecords.ToString());
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Placeholders by category", EditorStyles.boldLabel);
            foreach (var pair in PlaceholderAnalytics.CountByType(data).OrderByDescending(p => p.Value))
                EditorGUILayout.LabelField(pair.Key, pair.Value.ToString());

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Placeholders by scene or asset", EditorStyles.boldLabel);
            foreach (var pair in PlaceholderAnalytics.CountByScene(data).OrderByDescending(p => p.Value).Take(12))
                EditorGUILayout.LabelField(pair.Key, pair.Value.ToString(), EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            bool blockBuilds = EditorGUILayout.ToggleLeft("Block builds when tagged placeholders remain in enabled build scenes (project setting)", data.blockBuilds);
            if (blockBuilds != data.blockBuilds) AssetRegistry.SetBlockBuilds(blockBuilds);
            EditorGUILayout.HelpBox("Build blocking checks explicit PlaceholderTag components in enabled Build Settings scenes, using the saved scene files. This setting is stored in ProjectSettings/RenderGuard/registry.json, so it is shared through version control. Heuristic suggestions, asset-only records, missing references and missing scripts are advisory and are not build blockers.", MessageType.None);
        }

        private void DrawMetric(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(55));
            }
        }

        // ----- Placeholders --------------------------------------------------------------------------------

        private static bool IsSceneUnsaved(PlaceholderRecord record)
        {
            if (record.targetKind != "gameObject" || string.IsNullOrEmpty(record.sceneOrPrefab) ||
                !record.sceneOrPrefab.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return false;
            Scene scene = SceneManager.GetSceneByPath(record.sceneOrPrefab);
            return scene.IsValid() && scene.isLoaded && scene.isDirty;
        }

        private static string StatusLabel(PlaceholderRecord record)
        {
            if (record.missingFromProject && record.replacementStatus != "applied") return "removed from project";
            if (record.replacementStatus == "applied" && IsSceneUnsaved(record)) return "applied (scene not saved)";
            return record.replacementStatus;
        }

        private void RefreshVisibleRecords()
        {
            if (visibleRecordsVersion == AssetRegistry.ChangeCounter && visibleRecordsSearch == searchText) return;
            visibleRecords = AssetRegistry.Data.placeholders
                .Where(p => string.IsNullOrEmpty(searchText) ||
                    Contains(p.displayName, searchText) || Contains(p.sceneOrPrefab, searchText) ||
                    Contains(p.assetType, searchText) || Contains(p.assetId, searchText))
                .OrderBy(p => p.displayName).ToList();
            visibleRecordsVersion = AssetRegistry.ChangeCounter;
            visibleRecordsSearch = searchText;
        }

        private void DrawPlaceholders()
        {
            searchText = EditorGUILayout.TextField("Search", searchText);
            RefreshVisibleRecords();
            EditorGUILayout.LabelField(visibleRecords.Count + " matching placeholder record(s)", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(130));
            foreach (PlaceholderRecord record in visibleRecords)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(record.displayName, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(StatusLabel(record), EditorStyles.miniBoldLabel);
                    }
                    EditorGUILayout.LabelField(record.assetType + "  |  " + record.targetKind, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(record.sceneOrPrefab, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button(selectedAssetId == record.assetId ? "Selected" : "Manage Replacement", GUILayout.Width(170)))
                        SelectRecord(record);
                }
            }
            EditorGUILayout.EndScrollView();

            PlaceholderRecord selected = AssetRegistry.Find(selectedAssetId);
            if (selected == null) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Replacement manager", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(selected.displayName + "  |  " + selected.assetId, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Target: " + selected.sceneOrPrefab + (string.IsNullOrEmpty(selected.gameObjectPath) ? "" : "\n" + selected.gameObjectPath), EditorStyles.wordWrappedMiniLabel);

            if (selected.missingFromProject && selected.replacementStatus != "applied")
            {
                EditorGUILayout.HelpBox("A full scan no longer finds this placeholder (its tag or asset was removed). It counts as resolved. You can remove the record, or keep it for the history.", MessageType.Info);
                if (GUILayout.Button("Remove Record", GUILayout.Width(140)))
                {
                    string removeId = selected.assetId;
                    string removeName = selected.displayName;
                    Defer(() => ConfirmAndRemoveRecord(removeId, removeName));
                }
            }

            if (replacementSelection == null && !string.IsNullOrEmpty(selected.replacementAssetPath))
                replacementSelection = AssetDatabase.LoadMainAssetAtPath(selected.replacementAssetPath);
            replacementSelection = EditorGUILayout.ObjectField("Replacement asset", replacementSelection, typeof(UnityEngine.Object), false);
            string replacementPath = replacementSelection == null ? string.Empty : AssetDatabase.GetAssetPath(replacementSelection);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Link Replacement"))
                {
                    string id = selected.assetId;
                    string chosen = replacementPath;
                    Defer(() =>
                    {
                        PlaceholderRecord record = AssetRegistry.Find(id);
                        string validation;
                        if (!ReplacementService.ValidateReplacement(record, chosen, out validation))
                            SetMessage(validation, MessageType.Warning);
                        else
                        {
                            AssetRegistry.LinkReplacement(id, chosen);
                            SetMessage("Replacement linked. The original placeholder remains unchanged.", MessageType.Info);
                        }
                    });
                }
                if (GUILayout.Button("Verify Link"))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        string message;
                        bool success = ReplacementService.VerifyLink(AssetRegistry.Find(id), out message);
                        SetMessage(message, success ? MessageType.Info : MessageType.Warning);
                    });
                }
                if (GUILayout.Button("Unlink"))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        if (!EditorUtility.DisplayDialog("Unlink replacement?", "Remove the saved replacement link? This will not modify either asset.", "Unlink", "Cancel")) return;
                        AssetRegistry.UnlinkReplacement(id);
                        replacementSelection = null;
                        SetMessage("Replacement link removed. No scene objects were changed.", MessageType.Info);
                    });
                }
            }

            bool canApply = selected.targetKind == "gameObject" && selected.sceneOrPrefab != null && selected.sceneOrPrefab.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = canApply && !string.IsNullOrEmpty(selected.replacementAssetPath) && selected.replacementStatus != "applied";
                if (GUILayout.Button("Apply Replacement to Scene", GUILayout.Height(26)))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        bool confirm = EditorUtility.DisplayDialog("Apply replacement?",
                            "Replace the tagged GameObject with the selected prefab in the recorded scene?\n\nThe old object's children are moved under the replacement. References from other objects to the old object will be lost. Unity Undo can revert this while the action remains in the Undo history. The scene is marked dirty but is not saved automatically.",
                            "Apply Replacement", "Cancel");
                        if (!confirm) return;
                        string message;
                        bool success = ReplacementService.ApplyToScene(AssetRegistry.Find(id), out message);
                        SetMessage(message, success ? MessageType.Info : MessageType.Error);
                    });
                }
                GUI.enabled = true;
                if (GUILayout.Button("Locate", GUILayout.Width(75)))
                {
                    PlaceholderRecord target = selected;
                    Defer(() => LocateRecord(target));
                }
            }

            if (selected.targetKind == "asset")
            {
                DrawAssetRecordActions(selected);
            }
            else if (!canApply)
            {
                EditorGUILayout.HelpBox("Automatic replacement supports tagged GameObjects in scene files. For a tag inside a prefab asset, link and verify the replacement, edit the prefab by hand and remove the tag; the next full scan then counts the record as removed.", MessageType.Info);
            }

            DrawHistory(selected.assetId);
        }

        private void ConfirmAndRemoveRecord(string assetId, string displayName)
        {
            if (!EditorUtility.DisplayDialog("Remove record?",
                "Remove '" + displayName + "' from the registry? Its history entries are kept. Your project files are not touched.", "Remove", "Cancel")) return;
            AssetRegistry.RemoveRecord(assetId);
            selectedAssetId = null;
            SetMessage("Record removed.", MessageType.Info);
        }

        private void DrawAssetRecordActions(PlaceholderRecord selected)
        {
            string key = selected.assetId + "|" + selected.replacementAssetPath + "|" + selected.replacementAssetGuid + "|" + AssetRegistry.ChangeCounter;
            if (key != swapCheckKey)
            {
                swapCheckOk = AssetSwapService.CanSwap(selected, out swapCheckReason);
                swapCheckKey = key;
            }

            EditorGUILayout.HelpBox("Asset-only records are not applied automatically. 'Swap File In Place' overwrites the placeholder file with the linked replacement and keeps the placeholder's GUID, so everything that uses it picks up the new content (the original is backed up first). Or swap the asset yourself and mark the record as replaced.", MessageType.Info);
            if (!swapCheckOk && !string.IsNullOrEmpty(swapCheckReason))
                EditorGUILayout.HelpBox(swapCheckReason, MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = swapCheckOk;
                if (GUILayout.Button("Swap File In Place...", GUILayout.Height(24)))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        PlaceholderRecord record = AssetRegistry.Find(id);
                        if (record == null) return;
                        if (!EditorUtility.DisplayDialog("Swap file in place?", AssetSwapService.DescribeSwap(record), "Swap File", "Cancel")) return;
                        string message;
                        bool success = AssetSwapService.SwapInPlace(record, out message);
                        SetMessage(message, success ? MessageType.Info : MessageType.Error);
                    });
                }
                GUI.enabled = AssetSwapService.CanRestore(selected);
                if (GUILayout.Button("Restore Original File", GUILayout.Height(24)))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        PlaceholderRecord record = AssetRegistry.Find(id);
                        if (!EditorUtility.DisplayDialog("Restore the original file?", "Copy the backed-up original back over the placeholder file? The current content is overwritten.", "Restore", "Cancel")) return;
                        string message;
                        bool success = AssetSwapService.RestoreOriginal(record, out message);
                        SetMessage(message, success ? MessageType.Info : MessageType.Error);
                    });
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selected.replacementStatus != "applied" && !string.IsNullOrEmpty(selected.replacementAssetPath);
                if (GUILayout.Button("Mark as Replaced (done by hand)", GUILayout.Width(240)))
                {
                    string id = selected.assetId;
                    Defer(() =>
                    {
                        if (!EditorUtility.DisplayDialog("Mark as replaced?", "Confirm that the placeholder asset has been replaced in the project. This only updates the registry.", "Mark Replaced", "Cancel")) return;
                        AssetRegistry.MarkResolved(id, "Marked as replaced by the user. RenderGuard did not change any asset.");
                        SetMessage("Record marked as replaced.", MessageType.Info);
                    });
                }
                GUI.enabled = true;
                if (GUILayout.Button("Stop Tracking", GUILayout.Width(120)))
                {
                    string removeId = selected.assetId;
                    string removeName = selected.displayName;
                    Defer(() => ConfirmAndRemoveRecord(removeId, removeName));
                }
            }
        }

        private void DrawHistory(string assetId)
        {
            var history = AssetRegistry.Data.history.Where(h => h.placeholderId == assetId).Reverse().Take(12).ToList();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Recent replacement history", EditorStyles.boldLabel);
            if (history.Count == 0) EditorGUILayout.LabelField("No history yet.", EditorStyles.miniLabel);
            historyScroll = EditorGUILayout.BeginScrollView(historyScroll, GUILayout.Height(Mathf.Min(130, 22 + history.Count * 36)));
            foreach (PlaceholderHistoryEntry item in history)
            {
                EditorGUILayout.LabelField(item.action + "  |  " + item.occurredAtUtc, EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(item.details, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        // ----- Suggestions ---------------------------------------------------------------------------------

        private void RefreshVisibleSuggestions()
        {
            if (visibleSuggestionsVersion == AssetRegistry.ChangeCounter && Mathf.Approximately(visibleSuggestionsMinConfidence, minConfidence)) return;
            visibleSuggestions = AssetRegistry.Data.suggestions
                .Where(s => s.status == "pending" && s.confidence >= minConfidence)
                .OrderByDescending(s => s.confidence).ThenBy(s => s.displayName).ToList();
            visibleSuggestionsVersion = AssetRegistry.ChangeCounter;
            visibleSuggestionsMinConfidence = minConfidence;
        }

        private void DrawSuggestions()
        {
            EditorGUILayout.LabelField("Review before approving", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Suggestions are heuristic candidates based on whole words in names (and folders, for assets and prefab roots). They are not confirmed placeholders. Review each one; approval adds a tag to a GameObject or creates an asset-only registry record.", MessageType.Info);
            minConfidence = EditorGUILayout.Slider("Minimum confidence", minConfidence, 0f, 1f);
            RefreshVisibleSuggestions();

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(visibleSuggestions.Count / (float)SuggestionPageSize));
            suggestionPage = Mathf.Clamp(suggestionPage, 0, pageCount - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(visibleSuggestions.Count + " pending suggestion(s)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUI.enabled = suggestionPage > 0;
                if (GUILayout.Button("Previous", GUILayout.Width(70))) suggestionPage--;
                GUI.enabled = suggestionPage < pageCount - 1;
                if (GUILayout.Button("Next", GUILayout.Width(70))) suggestionPage++;
                GUI.enabled = true;
                EditorGUILayout.LabelField("Page " + (suggestionPage + 1) + " / " + pageCount, EditorStyles.miniLabel, GUILayout.Width(80));
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (PlaceholderSuggestion suggestion in visibleSuggestions.Skip(suggestionPage * SuggestionPageSize).Take(SuggestionPageSize))
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField(suggestion.displayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(suggestion.targetKind + "  |  " + suggestion.assetType + "  |  confidence " + Mathf.RoundToInt(suggestion.confidence * 100f) + "%", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(suggestion.targetKind == "asset" ? suggestion.assetPath : suggestion.contextPath + "\n" + suggestion.gameObjectPath, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField(suggestion.reason, EditorStyles.wordWrappedLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        PlaceholderSuggestion current = suggestion;
                        if (GUILayout.Button("Approve as Placeholder"))
                        {
                            Defer(() =>
                            {
                                bool confirm = EditorUtility.DisplayDialog("Approve suggestion?",
                                    "Approve '" + current.displayName + "' as a placeholder?\n\nFor prefab assets, RenderGuard creates a backup before saving the added tag.",
                                    "Approve", "Cancel");
                                if (!confirm) return;
                                string message;
                                bool success = PlaceholderSuggestionService.Approve(current, out message);
                                SetMessage(message, success ? MessageType.Info : MessageType.Warning);
                            });
                        }
                        if (GUILayout.Button("Dismiss"))
                            Defer(() => PlaceholderSuggestionService.Dismiss(current));
                        if (GUILayout.Button("Locate", GUILayout.Width(75)))
                            Defer(() => LocateSuggestion(current));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ----- Reports -------------------------------------------------------------------------------------

        private void DrawReports()
        {
            if (lastReport == null)
            {
                EditorGUILayout.HelpBox("Run Scan Entire Project or Scan Build Scenes to populate a report.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Latest scan report", EditorStyles.boldLabel);
            DrawMetric("Scope", lastReport.scanScope);
            DrawMetric("Scanned at (UTC)", lastReport.scannedAtUtc);
            DrawMetric("Tagged placeholders", lastReport.placeholderCount.ToString());
            DrawMetric("Scenes scanned", lastReport.scenesScanned.ToString());
            DrawMetric("Prefabs scanned", lastReport.prefabsScanned.ToString());
            DrawMetric("Candidate assets checked", lastReport.assetsChecked.ToString());
            DrawMetric("Pending suggestions", lastReport.suggestionCount.ToString());
            DrawMetric("Missing references", lastReport.missingReferenceCount.ToString());
            DrawMetric("Missing scripts", lastReport.missingScriptCount.ToString());
            DrawMetric("Duplicate IDs", lastReport.duplicateIdCount.ToString());
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export JSON")) { ScanReport report = lastReport; Defer(() => PlaceholderReportExporter.ExportJson(report)); }
                if (GUILayout.Button("Export CSV")) { ScanReport report = lastReport; Defer(() => PlaceholderReportExporter.ExportCsv(report)); }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Findings", EditorStyles.boldLabel);
            foreach (ScanFinding finding in lastReport.findings)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField(finding.displayName + "  |  " + finding.findingType, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(finding.sceneOrPrefab + "\n" + finding.gameObjectPath, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField(finding.message, EditorStyles.wordWrappedLabel);
                    if (finding.findingType == "tagged" && GUILayout.Button("Locate"))
                    {
                        ScanFinding current = finding;
                        Defer(() => LocateInContext(current.sceneOrPrefab, current.gameObjectPath, current.assetId));
                    }
                }
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Issues", EditorStyles.boldLabel);
            foreach (ScanIssue issue in lastReport.issues)
            {
                EditorGUILayout.HelpBox(issue.category + ": " + issue.message +
                    (string.IsNullOrEmpty(issue.contextPath) || issue.category == "duplicate-id" ? "" : "\n" + issue.contextPath) +
                    (string.IsNullOrEmpty(issue.objectPath) || issue.category == "duplicate-id" ? "" : "\n" + issue.objectPath) +
                    (string.IsNullOrEmpty(issue.propertyPath) ? "" : "\nProperty: " + issue.propertyPath),
                    issue.severity == "error" ? MessageType.Error : MessageType.Warning);
                if (issue.category == "duplicate-id" && !string.IsNullOrEmpty(issue.assetId) &&
                    GUILayout.Button("Give the extra scene copies new IDs", GUILayout.Width(260)))
                {
                    string duplicateId = issue.assetId;
                    Defer(() => FixDuplicateIds(duplicateId));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void FixDuplicateIds(string duplicateId)
        {
            if (lastReport == null) return;
            // Keep the first independently authored object; re-identify the rest (scene objects only).
            List<ScanFinding> authored = lastReport.findings
                .Where(f => f.findingType == "tagged" && f.assetId == duplicateId && !f.fromPrefabInstance).ToList();
            if (authored.Count < 2) { SetMessage("Nothing to fix for this ID.", MessageType.Info); return; }
            if (!EditorUtility.DisplayDialog("Give new IDs?",
                "Keep the first object with this ID and give " + (authored.Count - 1) + " other copy/copies a new ID?\n\nOnly scene objects are changed (via Undo). The scenes are marked dirty but not saved. Copies inside prefab assets must be fixed by hand.",
                "Give New IDs", "Cancel")) return;

            int fixedCount = 0;
            string lastMessage = string.Empty;
            for (int i = 1; i < authored.Count; i++)
            {
                string message;
                if (PlaceholderIdService.RegenerateId(authored[i].sceneOrPrefab, authored[i].gameObjectPath, duplicateId, out message)) fixedCount++;
                lastMessage = message;
            }
            SetMessage(fixedCount > 0
                ? "Gave " + fixedCount + " object(s) a new ID. Save the affected scenes, then scan again."
                : lastMessage, fixedCount > 0 ? MessageType.Info : MessageType.Warning);
        }

        // ----- Scans ---------------------------------------------------------------------------------------

        private void RunProjectScan()
        {
            try
            {
                lastReport = PlaceholderScanner.ScanProject();
                SetMessage("Project scan complete. Review findings and suggestions before taking action.", MessageType.Info);
            }
            catch (Exception ex) { SetMessage("Project scan failed: " + ex.Message, MessageType.Error); Debug.LogException(ex); }
        }

        private void RunBuildScan()
        {
            try
            {
                lastReport = PlaceholderScanner.ScanBuildScenes();
                SetMessage("Build-scene scan complete (read-only: the registry was not changed).", MessageType.Info);
            }
            catch (Exception ex) { SetMessage("Build-scene scan failed: " + ex.Message, MessageType.Error); Debug.LogException(ex); }
        }

        // ----- Selection and locate ------------------------------------------------------------------------

        private void SelectRecord(PlaceholderRecord record)
        {
            selectedAssetId = record.assetId;
            replacementSelection = string.IsNullOrEmpty(record.replacementAssetPath)
                ? null : AssetDatabase.LoadMainAssetAtPath(record.replacementAssetPath);
        }

        private void LocateRecord(PlaceholderRecord record)
        {
            if (record.targetKind == "asset") { PingAsset(record.sceneOrPrefab); return; }
            LocateInContext(record.sceneOrPrefab, record.gameObjectPath, record.assetId);
        }

        private void LocateSuggestion(PlaceholderSuggestion suggestion)
        {
            if (suggestion.targetKind == "asset") { PingAsset(suggestion.assetPath); return; }
            LocateInContext(suggestion.contextPath, suggestion.gameObjectPath, null);
        }

        private void PingAsset(string path)
        {
            UnityEngine.Object asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) { SetMessage("The asset could not be found: " + path, MessageType.Warning); return; }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void LocateInContext(string contextPath, string objectPath, string assetId)
        {
            if (string.IsNullOrEmpty(contextPath)) return;
            if (!contextPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                PingAsset(contextPath);
                SetMessage("Selected the asset. Open the prefab to find the object at '" + objectPath + "'.", MessageType.Info);
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(contextPath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try { scene = EditorSceneManager.OpenScene(contextPath, OpenSceneMode.Additive); openedHere = true; }
                catch (Exception ex) { SetMessage("Could not open scene: " + ex.Message, MessageType.Warning); return; }
            }

            GameObject[] roots = scene.GetRootGameObjects();
            Transform target = null;
            if (!string.IsNullOrEmpty(assetId))
            {
                bool ambiguous;
                target = PlaceholderScanner.FindTaggedTransform(roots, assetId, objectPath, out ambiguous);
            }
            if (target == null) target = PlaceholderScanner.FindTransformByPath(roots, objectPath);

            if (target == null)
            {
                SetMessage("Could not find the object. Run a fresh scan.", MessageType.Warning);
                return;
            }
            Selection.activeGameObject = target.gameObject;
            EditorGUIUtility.PingObject(target.gameObject);
            if (openedHere) SetMessage("Opened '" + contextPath + "' additively to select the object. Close the scene from the Hierarchy when you are done.", MessageType.Info);
        }

        private void SetMessage(string message, MessageType type)
        {
            statusMessage = message;
            statusMessageType = type;
            Repaint();
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
