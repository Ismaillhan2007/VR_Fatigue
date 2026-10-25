using System;
using System.Collections;
using System.Collections.Generic;
using App.Core.Contracts;
using App.Core.Analysis;
using App.Core.Flow;
using App.UI;
using App.UI.Panels;
using App.Reports;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace App.Tests
{
    /// <summary>Desktop smoke test: the generated Assessment scene completes one synthetic session offline.</summary>
    public class AssessmentFlowSmokeTest
    {
        [TestCase(null)]
        [TestCase("demo-access-token")]
        public void HttpClientBuildsCompatibleRequestWithOptionalRuntimeToken(string token)
        {
            var request = new AnalyzeRequest
            {
                SessionSummary = ContractJson.Parse<SessionSummary>(System.IO.File.ReadAllText(
                    System.IO.Path.Combine(Application.dataPath, "..", "..", "fixtures", "session_synthetic_usable.json"))),
                Preferences = null, Reconciliation = Reconciliation.AgreeHigh,
            };
            request.SessionSummary.SelfReport.FreeText = null;
            var client = new HttpAnalysisClient("http://127.0.0.1:8000/", token ?? "");
            using (var web = client.CreateRequest(request))
            {
                Assert.AreEqual("http://127.0.0.1:8000/api/v1/analyze", web.url);
                Assert.AreEqual("POST", web.method);
                Assert.AreEqual(15, web.timeout);
                Assert.AreEqual(token == null ? null : "Bearer " + token, web.GetRequestHeader("Authorization"));
                var parsed = ContractJson.Parse<AnalyzeRequest>(System.Text.Encoding.UTF8.GetString(web.uploadHandler.data));
                Assert.IsNull(parsed.Preferences);
                Assert.IsNull(parsed.SessionSummary.SelfReport.FreeText);
                if (token != null) StringAssert.DoesNotContain(token, client.Name);
            }
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds = 10)
        {
            var end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SyntheticSessionCompletesWithLabelledFallback()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            Assert.NotNull(c, "SessionController missing: run App > Build Assessment Scene");
            c.BaselineSeconds = 1;
            c.TaskSeconds = 1;
            c.Analysis = AnalysisMode.Http;
            c.BackendBaseUrl = "http://127.0.0.1:9"; // nothing listens here: must fall back locally
            Assert.AreEqual(FlowState.Consent, c.Flow.State);

            c.AcceptConsent();
            Assert.AreEqual(FlowState.Baseline, c.Flow.State);
            yield return WaitUntil(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn);

            c.SubmitCheckIn(new SelfReport
            {
                Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 4,
                Chips = new List<Chip> { Chip.TiredEyes, Chip.WantQuiet }, FreeText = "a bit dizzy",
            }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var ctx = c.Flow.Context;
            Assert.AreEqual(DataSource.Synthetic, ctx.Summary.DataSource);
            Assert.AreEqual(AiSource.Fallback, ctx.Analysis.AiSource);
            Assert.AreEqual(SafetyNotice.TakeBreak, ctx.Analysis.SafetyNotice);
            StringAssert.Contains("backend", ctx.AnalysisFallbackReason);
            var bar = UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>();
            StringAssert.Contains("SYNTHETIC", bar.DataLabel.text);
            StringAssert.Contains("LOCAL FALLBACK", bar.AiLabel.text);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);

            c.Exit();
            Assert.AreEqual(FlowState.Exited, c.Flow.State);
            Assert.IsNull(c.Flow.Context.Summary);
        }
    }
}
