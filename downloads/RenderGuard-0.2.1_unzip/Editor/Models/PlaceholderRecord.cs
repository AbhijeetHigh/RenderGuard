using System;
using System.Collections.Generic;

namespace RenderGuard.Editor
{
    [Serializable]
    public sealed class PlaceholderRegistryData
    {
        public const int CurrentSchemaVersion = 3;
        // 0 means the field was absent, which is how v1 registries look. New registries are stamped by AssetRegistry.
        public int schemaVersion = 0;
        // Project-wide switch (stored with the registry so the whole team shares it).
        public bool blockBuilds = true;
        public List<PlaceholderRecord> placeholders = new List<PlaceholderRecord>();
        public List<PlaceholderSuggestion> suggestions = new List<PlaceholderSuggestion>();
        public List<PlaceholderHistoryEntry> history = new List<PlaceholderHistoryEntry>();
    }

    [Serializable]
    public sealed class PlaceholderRecord
    {
        public string assetId;
        public string displayName;
        public string assetType;
        public string targetKind = "gameObject";
        public string createdAtUtc;
        public string updatedAtUtc;
        public string sceneOrPrefab;
        public string gameObjectPath;
        public string reason;
        public string replacementAssetPath;
        // Stored so a moved or renamed replacement asset can be found again.
        public string replacementAssetGuid;
        // Supported values: unlinked, linked, verified, applied.
        public string replacementStatus = "unlinked";
        public bool exemptFromBuildBlock;
        public string exemptionReason;
        // Set by a full project scan when the tag (or asset) behind this record no longer exists.
        public bool missingFromProject;
        public string missingSinceUtc;
        // Set by an in-place file swap: where the original placeholder file was backed up (so it can be restored).
        public string lastSwapBackupPath;
    }

    [Serializable]
    public sealed class PlaceholderSuggestion
    {
        public string suggestionId;
        public string suggestionKey;
        public string targetKind;
        public string assetPath;
        public string contextPath;
        public string gameObjectPath;
        public string displayName;
        public string assetType;
        public float confidence;
        public string reason;
        // pending, approved, dismissed
        public string status = "pending";
        public string firstSeenUtc;
        public string lastSeenUtc;
    }

    [Serializable]
    public sealed class PlaceholderHistoryEntry
    {
        public string occurredAtUtc;
        public string placeholderId;
        public string action;
        public string previousAssetPath;
        public string newAssetPath;
        public string contextPath;
        public string details;
    }

    [Serializable]
    public sealed class ScanReport
    {
        public string scannedAtUtc;
        public string scanScope;
        public int placeholderCount;
        public int suggestionCount;
        public int missingReferenceCount;
        public int missingScriptCount;
        public int duplicateIdCount;
        public int scenesScanned;
        public int prefabsScanned;
        public int assetsChecked;
        public List<ScanFinding> findings = new List<ScanFinding>();
        public List<ScanIssue> issues = new List<ScanIssue>();
        public List<PlaceholderSuggestion> suggestions = new List<PlaceholderSuggestion>();
    }

    [Serializable]
    public sealed class ScanFinding
    {
        public string findingType = "tagged";
        public string assetId;
        public string displayName;
        public string sceneOrPrefab;
        public string gameObjectPath;
        public string assetType;
        public string replacementStatus;
        public bool exemptFromBuildBlock;
        public string exemptionReason;
        // True when the tag lives on an instance of a prefab (its ID is shared with the prefab asset by design).
        public bool fromPrefabInstance;
        public string message;
    }

    [Serializable]
    public sealed class ScanIssue
    {
        public string severity = "warning";
        public string category;
        public string assetId;
        public string contextPath;
        public string objectPath;
        public string propertyPath;
        public string message;
    }
}
