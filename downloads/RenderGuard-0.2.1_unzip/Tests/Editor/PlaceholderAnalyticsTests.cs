using System.Collections.Generic;
using NUnit.Framework;

namespace RenderGuard.Editor.Tests
{
    public sealed class PlaceholderAnalyticsTests
    {
        [Test]
        public void EmptyRegistryHasFullReadiness()
        {
            PlaceholderAnalyticsSummary summary = PlaceholderAnalytics.BuildSummary(new PlaceholderRegistryData());
            Assert.AreEqual(100, summary.readinessScore);
            Assert.AreEqual(0, summary.totalTracked);
        }

        [Test]
        public void AppliedAndExemptRecordsCountAsResolved()
        {
            var data = new PlaceholderRegistryData
            {
                placeholders = new List<PlaceholderRecord>
                {
                    new PlaceholderRecord { assetId = "a", replacementStatus = "applied" },
                    new PlaceholderRecord { assetId = "b", replacementStatus = "unlinked", exemptFromBuildBlock = true, exemptionReason = "Required for this test scene" },
                    new PlaceholderRecord { assetId = "c", replacementStatus = "linked" }
                }
            };
            PlaceholderAnalyticsSummary summary = PlaceholderAnalytics.BuildSummary(data);
            Assert.AreEqual(67, summary.readinessScore);
            Assert.AreEqual(1, summary.unlinked);
            Assert.AreEqual(1, summary.linked);
            Assert.AreEqual(1, summary.applied);
        }

        [Test]
        public void ExemptionWithoutReasonDoesNotCountAsResolved()
        {
            var data = new PlaceholderRegistryData
            {
                placeholders = new List<PlaceholderRecord>
                {
                    new PlaceholderRecord { assetId = "a", replacementStatus = "unlinked", exemptFromBuildBlock = true, exemptionReason = "  " }
                }
            };
            Assert.AreEqual(0, PlaceholderAnalytics.BuildSummary(data).readinessScore);
        }

        [Test]
        public void RecordsRemovedFromTheProjectCountAsResolved()
        {
            var data = new PlaceholderRegistryData
            {
                placeholders = new List<PlaceholderRecord>
                {
                    new PlaceholderRecord { assetId = "gone", replacementStatus = "unlinked", missingFromProject = true },
                    new PlaceholderRecord { assetId = "still-here", replacementStatus = "linked" }
                }
            };
            PlaceholderAnalyticsSummary summary = PlaceholderAnalytics.BuildSummary(data);
            Assert.AreEqual(50, summary.readinessScore);
            Assert.AreEqual(1, summary.removed);
        }

        [Test]
        public void CountHelpersTolerateMissingData()
        {
            Assert.AreEqual(0, PlaceholderAnalytics.CountByType(null).Count);
            Assert.AreEqual(0, PlaceholderAnalytics.CountByScene(new PlaceholderRegistryData { placeholders = null }).Count);
        }
    }
}
