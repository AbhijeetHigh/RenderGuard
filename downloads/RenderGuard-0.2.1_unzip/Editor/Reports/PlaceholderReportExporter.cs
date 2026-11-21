using System.IO;
using System.Text;
using UnityEditor;

namespace RenderGuard.Editor
{
    public static class PlaceholderReportExporter
    {
        public static void ExportJson(ScanReport report)
        {
            string path = EditorUtility.SaveFilePanel("Export RenderGuard scan", "", "renderguard-report.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(report ?? new ScanReport(), true));
            EditorUtility.DisplayDialog("RenderGuard", "JSON scan report exported to:\n" + path, "OK");
        }

        public static void ExportCsv(ScanReport report)
        {
            string path = EditorUtility.SaveFilePanel("Export RenderGuard findings", "", "renderguard-findings.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var builder = new StringBuilder();
            builder.AppendLine("FindingType,AssetId,DisplayName,Context,GameObjectPath,AssetType,ReplacementStatus,Exempt,ExemptionReason,Severity,Category,PropertyPath,Message");
            if (report != null)
            {
                foreach (ScanFinding finding in report.findings ?? new System.Collections.Generic.List<ScanFinding>())
                {
                    builder.AppendLine(string.Join(",", new[]
                    {
                        Csv(finding.findingType), Csv(finding.assetId), Csv(finding.displayName), Csv(finding.sceneOrPrefab),
                        Csv(finding.gameObjectPath), Csv(finding.assetType), Csv(finding.replacementStatus),
                        Csv(finding.exemptFromBuildBlock.ToString()), Csv(finding.exemptionReason), "", "", "", Csv(finding.message)
                    }));
                }
                foreach (ScanIssue issue in report.issues ?? new System.Collections.Generic.List<ScanIssue>())
                {
                    builder.AppendLine(string.Join(",", new[]
                    {
                        "issue", "", "", Csv(issue.contextPath), Csv(issue.objectPath), "", "", "", "",
                        Csv(issue.severity), Csv(issue.category), Csv(issue.propertyPath), Csv(issue.message)
                    }));
                }
                if (report.suggestions != null)
                {
                    foreach (PlaceholderSuggestion suggestion in report.suggestions)
                    {
                        builder.AppendLine(string.Join(",", new[]
                        {
                            "suggestion", Csv(suggestion.suggestionId), Csv(suggestion.displayName),
                            Csv(suggestion.targetKind == "asset" ? suggestion.assetPath : suggestion.contextPath),
                            Csv(suggestion.gameObjectPath), Csv(suggestion.assetType), Csv(suggestion.status), "", "",
                            Csv("confidence " + suggestion.confidence.ToString("0.00")), Csv(suggestion.targetKind), "", Csv(suggestion.reason)
                        }));
                    }
                }
            }
            // UTF-8 with BOM so Excel reads non-ASCII names and paths correctly.
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            EditorUtility.DisplayDialog("RenderGuard", "CSV report exported to:\n" + path, "OK");
        }

        private static string Csv(string value)
        {
            string text = value ?? string.Empty;
            // Neutralise spreadsheet formulas: a cell that starts with = + - @ (or a tab/CR) would otherwise be evaluated by Excel.
            if (text.Length > 0 && (text[0] == '=' || text[0] == '+' || text[0] == '-' || text[0] == '@' || text[0] == '\t' || text[0] == '\r'))
                text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
