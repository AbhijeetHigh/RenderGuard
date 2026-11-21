using NUnit.Framework;

namespace RenderGuard.Editor.Tests
{
    public sealed class PlaceholderHeuristicsTests
    {
        private static bool Matches(string name, string path = "Assets/Scenes/Level.unity", bool checkFolders = false)
        {
            float confidence;
            string reason;
            return PlaceholderHeuristics.Evaluate(name, path, checkFolders, out confidence, out reason);
        }

        [TestCase("Temple")]
        [TestCase("Tempest")]
        [TestCase("Tempo")]
        [TestCase("Temperature")]
        [TestCase("Swipe")]
        [TestCase("SwipeLeft")]
        [TestCase("Wipe")]
        [TestCase("Door_Template")]
        [TestCase("Template")]
        public void OrdinaryNamesAreNotFlagged(string name)
        {
            Assert.IsFalse(Matches(name), name);
        }

        [TestCase("Temp_Door")]
        [TestCase("TempDoor")]
        [TestCase("temp")]
        [TestCase("Door_temp")]
        [TestCase("WIP_Door")]
        [TestCase("WIPDoor")]
        [TestCase("Placeholder_Crate")]
        [TestCase("PlaceholderCrate")]
        [TestCase("dummy")]
        [TestCase("ProxyLight")]
        [TestCase("Greybox Wall")]
        [TestCase("blockout01")]
        public void PlaceholderWordsAreFlagged(string name)
        {
            Assert.IsTrue(Matches(name), name);
        }

        [Test]
        public void StrongWordsScoreHigherThanWeakOnes()
        {
            float strong, weak;
            string reason;
            PlaceholderHeuristics.Evaluate("Placeholder_Crate", "", false, out strong, out reason);
            PlaceholderHeuristics.Evaluate("Temp_Crate", "", false, out weak, out reason);
            Assert.Greater(strong, weak);
        }

        [Test]
        public void FoldersOnlyCountWhenRequestedAndOnlyForDirectories()
        {
            Assert.IsTrue(Matches("Crate", "Assets/Prototypes/Crate.prefab", true));
            Assert.IsFalse(Matches("Crate", "Assets/Prototypes/Crate.prefab", false));
            Assert.IsFalse(Matches("Wall", "Assets/Temple/Wall.png", true));
            // The file name is not a folder: "Temp.png" in a normal folder must not match through the folder rule.
            Assert.IsFalse(Matches("x", "Assets/Art/Temp.png", true));
        }

        [Test]
        public void TokenizerSplitsCamelCaseAcronymsAndDigits()
        {
            CollectionAssert.AreEqual(new[] { "wip", "door" }, PlaceholderHeuristics.Tokenize("WIPDoor"));
            CollectionAssert.AreEqual(new[] { "temp", "door", "01" }, PlaceholderHeuristics.Tokenize("TempDoor01"));
            CollectionAssert.AreEqual(new[] { "door", "template" }, PlaceholderHeuristics.Tokenize("Door_Template"));
        }
    }
}
