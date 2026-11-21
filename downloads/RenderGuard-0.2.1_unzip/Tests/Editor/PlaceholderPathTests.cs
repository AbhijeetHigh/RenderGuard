using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RenderGuard.Editor.Tests
{
    public sealed class PlaceholderPathTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            created.Add(go);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private GameObject MakeRoot()
        {
            // A unique name keeps these tests independent of whatever else is open in the editor.
            return Make("PG_TestRoot_" + Guid.NewGuid().ToString("N"));
        }

        [Test]
        public void SiblingsWithTheSameNameGetDistinctPaths()
        {
            GameObject root = MakeRoot();
            GameObject first = Make("Dummy", root.transform);
            GameObject second = Make("Dummy", root.transform);
            GameObject other = Make("Other", root.transform);

            Dictionary<Transform, string> map = PlaceholderScanner.BuildPathMap(new[] { root });

            Assert.AreEqual(root.name + "/Dummy[0]", map[first.transform]);
            Assert.AreEqual(root.name + "/Dummy[1]", map[second.transform]);
            Assert.AreEqual(root.name + "/Other", map[other.transform]);
            Assert.AreEqual(map[second.transform], PlaceholderScanner.GetHierarchyPath(second.transform));
        }

        [Test]
        public void SlashesInNamesAreEscaped()
        {
            GameObject root = MakeRoot();
            GameObject child = Make("a/b", root.transform);
            Assert.AreEqual(root.name + "/a%2Fb", PlaceholderScanner.BuildPathMap(new[] { root })[child.transform]);
        }

        [Test]
        public void FindTransformByPathReturnsTheExactSibling()
        {
            GameObject root = MakeRoot();
            Make("Dummy", root.transform);
            GameObject second = Make("Dummy", root.transform);
            Transform found = PlaceholderScanner.FindTransformByPath(new[] { root }, root.name + "/Dummy[1]");
            Assert.AreSame(second.transform, found);
        }

        [Test]
        public void FindTaggedTransformUsesTheIdAndOnlyUsesThePathToBreakTies()
        {
            GameObject root = MakeRoot();
            GameObject a = Make("Crate", root.transform);
            GameObject b = Make("Crate", root.transform);
            a.AddComponent<PlaceholderTag>().Configure("unique", "A", PlaceholderAssetType.Other, "");
            b.AddComponent<PlaceholderTag>().Configure("shared", "B", PlaceholderAssetType.Other, "");
            GameObject c = Make("Crate", root.transform);
            c.AddComponent<PlaceholderTag>().Configure("shared", "C", PlaceholderAssetType.Other, "");

            bool ambiguous;
            // Unique ID: found even if the stored path is stale (the object was renamed or moved).
            Assert.AreSame(a.transform, PlaceholderScanner.FindTaggedTransform(new[] { root }, "unique", "stale/path", out ambiguous));
            Assert.IsFalse(ambiguous);

            // Shared ID: the path decides.
            Assert.AreSame(c.transform, PlaceholderScanner.FindTaggedTransform(new[] { root }, "shared", root.name + "/Crate[2]", out ambiguous));
            Assert.IsFalse(ambiguous);

            // Shared ID and a path that matches neither: refuse to guess.
            Assert.IsNull(PlaceholderScanner.FindTaggedTransform(new[] { root }, "shared", "nowhere", out ambiguous));
            Assert.IsTrue(ambiguous);
        }
    }
}
