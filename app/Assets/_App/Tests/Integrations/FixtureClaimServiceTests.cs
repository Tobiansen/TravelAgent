using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TravelAgent.Integrations.Claims;
using UnityEngine;

namespace TravelAgent.Tests.Integrations
{
    public sealed class FixtureClaimServiceTests
    {
        [Test]
        public void FinnairInitialReportHasSourcedPhotoRequirementAndHonestGuidanceFallback()
        {
            GuidanceResponse response = Run(new ClaimResearchRequest());

            Assert.That(response.origin, Is.EqualTo("fixture"));
            Assert.That(response.status, Is.EqualTo("needsConfirmation"));
            Assert.That(response.retrievedAtUtc, Is.EqualTo(FixtureClaimService.CapturedAtUtc));
            Assert.That(response.reportingUrl, Is.EqualTo(FixtureClaimService.OfficialUrl));
            Assert.That(response.reportingSourceUrl, Is.EqualTo(FixtureClaimService.OfficialUrl));
            Assert.That(response.reportingTargetKind, Is.EqualTo("official-guidance"));
            Assert.That(response.items.All(item => item.sourceUrl == FixtureClaimService.OfficialUrl), Is.True);
            Assert.That(response.items.Any(item => item.kind == "evidence" && item.text.Contains("required for the initial report")), Is.True);
            Assert.That(response.items.Any(item => item.kind == "step" && item.text.Contains("report number")), Is.True);
            Assert.That(response.items.Any(item => item.kind == "limitation" && item.text.Contains("follows the initial report")), Is.True);
            Assert.That(response.items.Any(item => item.kind == "deadline"), Is.False);
            Assert.That(response.missingContext.Any(question => question.Contains("not been independently verified")), Is.True);
        }

        [Test]
        public void DelayedOrUnsupportedCaseCannotReuseTheDamagedBagFixture()
        {
            foreach (ClaimResearchRequest request in new[] {
                new ClaimResearchRequest { issueType = "delayed" },
                new ClaimResearchRequest { airline = "Other Air" }
            })
            {
                GuidanceResponse response = Run(request);
                Assert.That(response.items, Is.Empty);
                Assert.That(response.sources, Is.Empty);
                Assert.That(response.reportingUrl, Is.Empty);
                Assert.That(response.status, Is.EqualTo("needsConfirmation"));
                Assert.That(response.error, Is.Not.Empty);
            }
        }

        [Test]
        public void OptionalReportingMetadataRoundTripsWithoutExpandingPrivateResearchRequest()
        {
            string[] fields = typeof(ClaimResearchRequest).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name).ToArray();
            Assert.That(fields, Is.EquivalentTo(new[] { "airline", "country", "route", "issueType", "collectionDate" }));
            GuidanceResponse response = JsonUtility.FromJson<GuidanceResponse>(JsonUtility.ToJson(Run(new ClaimResearchRequest())));
            Assert.That(response.schemaVersion, Is.EqualTo("1"));
            Assert.That(response.reportingTargetKind, Is.EqualTo("official-guidance"));
            Assert.That(response.reportingSourceUrl, Is.EqualTo(FixtureClaimService.OfficialUrl));
        }

        private static GuidanceResponse Run(ClaimResearchRequest request)
        {
            GuidanceResponse response = null;
            var routine = new FixtureClaimService().Research(request, value => response = value);
            while (routine.MoveNext()) { }
            Assert.That(response, Is.Not.Null);
            return response;
        }
    }
}
