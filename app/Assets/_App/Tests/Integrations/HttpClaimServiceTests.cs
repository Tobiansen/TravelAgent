using System;
using NUnit.Framework;
using TravelAgent.Integrations.Claims;

namespace TravelAgent.Tests.Integrations
{
    public sealed class HttpClaimServiceTests
    {
        [Test]
        public void SynchronousHttpPolicyFailureCompletesOnceWithoutLeavingTheUiWaiting()
        {
            const string privateToken = "test-session-value-never-display";
            int sendCalls = 0;
            var service = new HttpClaimService("http://10.110.242.22:8788", privateToken, _ => {
                sendCalls++;
                throw new InvalidOperationException("Insecure connection not allowed; " + privateToken);
            });
            int completions = 0;
            GuidanceResponse response = null;
            var routine = service.Research(new ClaimResearchRequest { airline = "Finnair" }, value => {
                completions++;
                response = value;
            });

            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(sendCalls, Is.EqualTo(1));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(response, Is.Not.Null);
            Assert.That(response.status, Is.EqualTo("error"));
            Assert.That(response.origin, Is.EqualTo("live"));
            Assert.That(response.airline, Is.EqualTo("Finnair"));
            Assert.That(response.error, Does.Contain("HTTP"));
            Assert.That(response.error, Does.Contain("HTTPS"));
            Assert.That(response.error, Does.Not.Contain(privateToken));
            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void OtherSynchronousRuntimeFailureCompletesOnceWithSafeRetryMessage()
        {
            const string privateDetail = "private-request-detail";
            var service = new HttpClaimService("https://gateway.example", "", _ => {
                throw new NotSupportedException(privateDetail);
            });
            int completions = 0;
            GuidanceResponse response = null;
            var routine = service.Research(new ClaimResearchRequest(), value => {
                completions++;
                response = value;
            });

            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(response.status, Is.EqualTo("error"));
            Assert.That(response.error, Does.Contain("retry"));
            Assert.That(response.error, Does.Not.Contain(privateDetail));
            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void MissingSendOperationCompletesOnceRatherThanYieldingAnUnstartedRequest()
        {
            var service = new HttpClaimService("https://gateway.example", "", _ => null);
            int completions = 0;
            GuidanceResponse response = null;
            var routine = service.Research(new ClaimResearchRequest(), value => {
                completions++;
                response = value;
            });

            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(response.status, Is.EqualTo("error"));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(completions, Is.EqualTo(1));
        }
    }
}
