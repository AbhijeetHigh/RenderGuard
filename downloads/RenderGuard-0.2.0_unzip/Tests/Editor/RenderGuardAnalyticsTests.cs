using System.Collections.Generic;
using NUnit.Framework;

namespace RenderGuard.Editor.Tests
{
    public sealed class RenderGuardAnalyticsTests
    {
        [Test]
        public void EmptyRegistryHasFullReadiness()
        {
            RenderGuardAnalyticsSummary summary = RenderGuardAnalytics.BuildSummary(new PlaceholderRegistryData());
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
            RenderGuardAnalyticsSummary summary = RenderGuardAnalytics.BuildSummary(data);
            Assert.AreEqual(67, summary.readinessScore);
            Assert.AreEqual(1, summary.unlinked);
            Assert.AreEqual(1, summary.linked);
            Assert.AreEqual(1, summary.applied);
        }
    }
}
