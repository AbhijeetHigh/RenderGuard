using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RenderGuard.Editor.Tests
{
    /// <summary>
    /// These tests create small real assets under Assets/__RenderGuardTests__ and delete them afterwards.
    /// The registry is redirected to a temporary file, so the project's real registry is never touched.
    /// </summary>
    public sealed class AssetSwapServiceTests
    {
        private const string Folder = "Assets/__RenderGuardTests__";
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "RenderGuardSwapTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            AssetRegistry.UseRegistryPathForTests(Path.Combine(tempDirectory, "registry.json"));
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__RenderGuardTests__");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetRegistry.UseRegistryPathForTests(null);
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        private static string MakePng(string name, Color32 color)
        {
            string path = Folder + "/" + name + ".png";
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }

        private static PlaceholderRecord TrackAndLink(string placeholderPath, string replacementPath, string category)
        {
            PlaceholderRecord record = AssetRegistry.RegisterAsset(placeholderPath, "placeholder", category, "test");
            AssetRegistry.LinkReplacement(record.assetId, replacementPath);
            return AssetRegistry.Find(record.assetId);
        }

        [Test]
        public void SwapKeepsTheGuidAndReplacesTheContentAndBacksUpTheOriginal()
        {
            string placeholder = MakePng("placeholder", new Color32(255, 0, 255, 255));
            string replacement = MakePng("final", new Color32(0, 200, 0, 255));
            string guidBefore = AssetDatabase.AssetPathToGUID(placeholder);
            byte[] original = File.ReadAllBytes(placeholder);
            byte[] expected = File.ReadAllBytes(replacement);
            PlaceholderRecord record = TrackAndLink(placeholder, replacement, "Texture");

            string message;
            bool ok = AssetSwapService.SwapInPlace(record, out message);

            Assert.IsTrue(ok, message);
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(placeholder), "The GUID must not change.");
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(placeholder));
            Assert.IsTrue(File.Exists(replacement), "The replacement file must be left in place.");
            PlaceholderRecord saved = AssetRegistry.Find(record.assetId);
            Assert.AreEqual("applied", saved.replacementStatus);
            Assert.IsTrue(File.Exists(saved.lastSwapBackupPath), "The original must be backed up.");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(saved.lastSwapBackupPath));
        }

        [Test]
        public void RestoreBringsTheOriginalBack()
        {
            string placeholder = MakePng("placeholder", new Color32(255, 0, 255, 255));
            string replacement = MakePng("final", new Color32(0, 200, 0, 255));
            byte[] original = File.ReadAllBytes(placeholder);
            PlaceholderRecord record = TrackAndLink(placeholder, replacement, "Texture");
            string message;
            Assert.IsTrue(AssetSwapService.SwapInPlace(record, out message), message);

            PlaceholderRecord swapped = AssetRegistry.Find(record.assetId);
            Assert.IsTrue(AssetSwapService.CanRestore(swapped));
            Assert.IsTrue(AssetSwapService.RestoreOriginal(swapped, out message), message);

            CollectionAssert.AreEqual(original, File.ReadAllBytes(placeholder));
            PlaceholderRecord restored = AssetRegistry.Find(record.assetId);
            Assert.AreEqual("linked", restored.replacementStatus);
            Assert.IsFalse(AssetSwapService.CanRestore(restored));
        }

        [Test]
        public void DifferentFileTypesAreRefusedAndNothingIsChanged()
        {
            string placeholder = MakePng("placeholder", new Color32(255, 0, 255, 255));
            string textPath = Folder + "/note.txt";
            File.WriteAllText(textPath, "not an image");
            AssetDatabase.ImportAsset(textPath, ImportAssetOptions.ForceUpdate);
            byte[] before = File.ReadAllBytes(placeholder);
            PlaceholderRecord record = TrackAndLink(placeholder, textPath, "Other");

            string message;
            Assert.IsFalse(AssetSwapService.SwapInPlace(record, out message));
            StringAssert.Contains("file types differ", message);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(placeholder));
        }

        [Test]
        public void ARecordWithoutALinkedReplacementCannotBeSwapped()
        {
            string placeholder = MakePng("placeholder", new Color32(255, 0, 255, 255));
            PlaceholderRecord record = AssetRegistry.RegisterAsset(placeholder, "placeholder", "Texture", "test");
            string reason;
            Assert.IsFalse(AssetSwapService.CanSwap(record, out reason));
            StringAssert.Contains("Link a replacement", reason);
        }

        [Test]
        public void ATaggedGameObjectRecordIsNotSwappedThisWay()
        {
            var record = new PlaceholderRecord { assetId = "go", targetKind = "gameObject", replacementAssetPath = "Assets/x.png" };
            string reason;
            Assert.IsFalse(AssetSwapService.CanSwap(record, out reason));
            StringAssert.Contains("asset-only", reason);
        }

        [Test]
        public void TrackingMenuFilesTexturesAndAudioUnderTheRightCategory()
        {
            string png = MakePng("anything", new Color32(1, 2, 3, 255));
            Assert.AreEqual("Texture", AssetTrackingMenu.CategoryFor(png));
            Assert.AreEqual("Other", AssetTrackingMenu.CategoryFor("Assets/DoesNotExist/Nothing.xyz"));
        }
    }
}
