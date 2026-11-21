using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace RenderGuard.Editor.Tests
{
    /// <summary>All tests use a temporary registry file; the real ProjectSettings registry is never touched.</summary>
    public sealed class AssetRegistryTests
    {
        private string tempDirectory;
        private string registryFile;
        private readonly List<GameObject> created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "RenderGuardTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            registryFile = Path.Combine(tempDirectory, "registry.json");
            AssetRegistry.UseRegistryPathForTests(registryFile);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
            AssetRegistry.UseRegistryPathForTests(null);
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        private static Dictionary<string, List<KeyValuePair<string, string>>> Seen(params string[] triples)
        {
            var result = new Dictionary<string, List<KeyValuePair<string, string>>>();
            for (int i = 0; i + 2 < triples.Length; i += 3)
            {
                if (!result.ContainsKey(triples[i])) result[triples[i]] = new List<KeyValuePair<string, string>>();
                result[triples[i]].Add(new KeyValuePair<string, string>(triples[i + 1], triples[i + 2]));
            }
            return result;
        }

        [Test]
        public void RegistryWithoutSchemaFieldIsTreatedAsOldAndBackedUpBeforeUpgrade()
        {
            File.WriteAllText(registryFile, "{\"placeholders\":[{\"assetId\":\"x\",\"displayName\":\"Old\",\"replacementStatus\":\"linked\"}]}");
            AssetRegistry.Load();
            Assert.AreEqual(1, AssetRegistry.Data.placeholders.Count);
            Assert.AreEqual("linked", AssetRegistry.Data.placeholders[0].replacementStatus);

            AssetRegistry.Save();

            string[] backups = Directory.GetFiles(tempDirectory, "registry.json.pre-v*-backup-*");
            Assert.AreEqual(1, backups.Length, "A backup of the old registry must be created before it is rewritten.");
            StringAssert.Contains("\"displayName\":\"Old\"", File.ReadAllText(backups[0]));
            StringAssert.Contains("\"schemaVersion\": " + PlaceholderRegistryData.CurrentSchemaVersion, File.ReadAllText(registryFile));
        }

        [Test]
        public void RegistryIsCopiedFromTheFormerLocationOnFirstLoad()
        {
            string legacyFile = Path.Combine(tempDirectory, "legacy", "registry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyFile));
            File.WriteAllText(legacyFile, "{\"schemaVersion\":3,\"blockBuilds\":false,\"placeholders\":[{\"assetId\":\"x\",\"displayName\":\"Old\",\"replacementStatus\":\"linked\"}]}");
            string newFile = Path.Combine(tempDirectory, "new", "registry.json");

            AssetRegistry.UseRegistryPathForTests(newFile, legacyFile);
            AssetRegistry.Load();

            Assert.IsTrue(File.Exists(newFile), "The registry must be copied to the new location.");
            Assert.AreEqual(1, AssetRegistry.Data.placeholders.Count);
            Assert.AreEqual("linked", AssetRegistry.Data.placeholders[0].replacementStatus);
            Assert.IsFalse(AssetRegistry.Data.blockBuilds);
            Assert.IsTrue(File.Exists(legacyFile), "The old file must be left in place.");
        }

        [Test]
        public void ExistingRegistryIsNeverOverwrittenByTheFormerOne()
        {
            string legacyFile = Path.Combine(tempDirectory, "legacy", "registry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyFile));
            File.WriteAllText(legacyFile, "{\"schemaVersion\":3,\"placeholders\":[{\"assetId\":\"old\"}]}");
            string newFile = Path.Combine(tempDirectory, "new", "registry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(newFile));
            File.WriteAllText(newFile, "{\"schemaVersion\":3,\"placeholders\":[{\"assetId\":\"current\"}]}");

            AssetRegistry.UseRegistryPathForTests(newFile, legacyFile);
            AssetRegistry.Load();

            Assert.AreEqual(1, AssetRegistry.Data.placeholders.Count);
            Assert.AreEqual("current", AssetRegistry.Data.placeholders[0].assetId);
        }

        [Test]
        public void SavingAnUnchangedRegistryDoesNotRewriteTheFile()
        {
            AssetRegistry.Data.placeholders.Add(new PlaceholderRecord { assetId = "a", displayName = "A" });
            AssetRegistry.Save();
            var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(registryFile, old);

            AssetRegistry.Save();

            Assert.AreEqual(old, File.GetLastWriteTimeUtc(registryFile));
        }

        [Test]
        public void BlockBuildsSettingIsStoredInTheRegistry()
        {
            AssetRegistry.SetBlockBuilds(false);
            AssetRegistry.Load();
            Assert.IsFalse(AssetRegistry.Data.blockBuilds);
            AssetRegistry.SetBlockBuilds(true);
            AssetRegistry.Load();
            Assert.IsTrue(AssetRegistry.Data.blockBuilds);
        }

        [Test]
        public void RegisteringAnUnchangedTagTwiceLeavesTheRecordUntouched()
        {
            var go = new GameObject("PG_Tag_" + Guid.NewGuid().ToString("N"));
            created.Add(go);
            PlaceholderTag tag = go.AddComponent<PlaceholderTag>();
            tag.Configure("tag-1", "Crate", PlaceholderAssetType.Model, "blockout");

            PlaceholderRecord first = AssetRegistry.Register(tag, "Assets/Scenes/A.unity", "Crate", false);
            string stamp = first.updatedAtUtc;
            System.Threading.Thread.Sleep(20);
            PlaceholderRecord second = AssetRegistry.Register(tag, "Assets/Scenes/A.unity", "Crate", false);

            Assert.AreSame(first, second);
            Assert.AreEqual(stamp, second.updatedAtUtc);
            Assert.AreEqual(1, AssetRegistry.Data.placeholders.Count);
        }

        [Test]
        public void FullScanFlagsVanishedTagsButNotAppliedOutOfScopeOrFailedOnes()
        {
            var data = AssetRegistry.Data;
            data.placeholders.Add(new PlaceholderRecord { assetId = "vanished", sceneOrPrefab = "Assets/Scenes/A.unity", gameObjectPath = "A", replacementStatus = "linked" });
            data.placeholders.Add(new PlaceholderRecord { assetId = "applied", sceneOrPrefab = "Assets/Scenes/A.unity", gameObjectPath = "B", replacementStatus = "applied" });
            data.placeholders.Add(new PlaceholderRecord { assetId = "present", sceneOrPrefab = "Assets/Scenes/C.unity", gameObjectPath = "C", replacementStatus = "unlinked" });
            data.placeholders.Add(new PlaceholderRecord { assetId = "outside", sceneOrPrefab = "Packages/x/Scene.unity", gameObjectPath = "D", replacementStatus = "unlinked" });
            data.placeholders.Add(new PlaceholderRecord { assetId = "failed", sceneOrPrefab = "Assets/Scenes/Broken.unity", gameObjectPath = "E", replacementStatus = "unlinked" });

            AssetRegistry.ReconcileAfterFullScan(
                Seen("present", "Assets/Scenes/C.unity", "C"),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Assets/Scenes/Broken.unity" },
                new HashSet<string>());

            Assert.IsTrue(AssetRegistry.Find("vanished").missingFromProject);
            Assert.IsFalse(AssetRegistry.Find("applied").missingFromProject);
            Assert.IsFalse(AssetRegistry.Find("present").missingFromProject);
            Assert.IsFalse(AssetRegistry.Find("outside").missingFromProject);
            Assert.IsFalse(AssetRegistry.Find("failed").missingFromProject);
            Assert.IsTrue(data.history.Any(h => h.placeholderId == "vanished" && h.action == "tag-missing"));
        }

        [Test]
        public void MovedTagMakesTheRecordFollowIt()
        {
            AssetRegistry.Data.placeholders.Add(new PlaceholderRecord { assetId = "mover", sceneOrPrefab = "Assets/Scenes/Old.unity", gameObjectPath = "Old/F", replacementStatus = "linked" });

            AssetRegistry.ReconcileAfterFullScan(Seen("mover", "Assets/Scenes/New.unity", "New/F"), new HashSet<string>(), new HashSet<string>());

            PlaceholderRecord record = AssetRegistry.Find("mover");
            Assert.AreEqual("Assets/Scenes/New.unity", record.sceneOrPrefab);
            Assert.AreEqual("New/F", record.gameObjectPath);
            Assert.IsFalse(record.missingFromProject);
        }

        [Test]
        public void StalePendingSuggestionsAreDroppedButDecisionsAreKept()
        {
            var data = AssetRegistry.Data;
            data.suggestions.Add(new PlaceholderSuggestion { suggestionId = "1", suggestionKey = "k1", status = "pending" });
            data.suggestions.Add(new PlaceholderSuggestion { suggestionId = "2", suggestionKey = "k2", status = "pending" });
            data.suggestions.Add(new PlaceholderSuggestion { suggestionId = "3", suggestionKey = "k3", status = "dismissed" });

            AssetRegistry.ReconcileAfterFullScan(Seen(), new HashSet<string>(), new HashSet<string> { "k1" });

            CollectionAssert.AreEquivalent(new[] { "1", "3" }, data.suggestions.Select(s => s.suggestionId).ToArray());
        }

        [Test]
        public void MarkingAnAssetRecordReplacedCountsTowardReadiness()
        {
            AssetRegistry.Data.placeholders.Add(new PlaceholderRecord
            {
                assetId = "asset:0123", targetKind = "asset", sceneOrPrefab = "Assets/Art/temp.png", replacementStatus = "linked"
            });
            Assert.AreEqual(0, PlaceholderAnalytics.BuildSummary(AssetRegistry.Data).readinessScore);

            AssetRegistry.MarkResolved("asset:0123", "swapped by hand");

            Assert.AreEqual(100, PlaceholderAnalytics.BuildSummary(AssetRegistry.Data).readinessScore);
        }

        [Test]
        public void RemovingARecordKeepsItsHistory()
        {
            AssetRegistry.Data.placeholders.Add(new PlaceholderRecord { assetId = "r", displayName = "R", replacementStatus = "unlinked" });
            AssetRegistry.RemoveRecord("r");
            Assert.IsNull(AssetRegistry.Find("r"));
            Assert.IsTrue(AssetRegistry.Data.history.Any(h => h.placeholderId == "r" && h.action == "record-removed"));
        }

        [Test]
        public void FailedLinkLeavesAnAppliedRecordUntouched()
        {
            // A path that cannot load must be rejected without changing the status.
            AssetRegistry.Data.placeholders.Add(new PlaceholderRecord { assetId = "ap", replacementStatus = "applied" });
            Assert.Throws<FileNotFoundException>(() => AssetRegistry.LinkReplacement("ap", "Assets/DoesNotExist/Nothing.prefab"));
            Assert.AreEqual("applied", AssetRegistry.Find("ap").replacementStatus);
        }
    }
}
