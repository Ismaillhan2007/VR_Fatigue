using System;
using System.Collections;
using System.Collections.Generic;
using App.Capture;
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
        [UnityTest]
        public IEnumerator CalculationFailureShowsUnavailableMeasurements()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.EyeSource = EyeSourceMode.Synthetic;
            c.BaselineSeconds = c.TaskSeconds = 1;
            c.Analysis = AnalysisMode.Local;
            c.AcceptConsent();
            yield return WaitUntil(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn);

            // A corrupt provider timestamp must not turn a failed calculation into canned measurements.
            var task = (App.Core.Metrics.PhaseRecording)typeof(SessionController)
                .GetField("_task", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(c);
            var row = task.Samples[0]; row.SensorTimestamp = double.NaN; task.Samples[0] = row;
            c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 3 }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null);

            var summary = c.Flow.Context.Summary;
            Assert.AreEqual(QualityStatus.InsufficientData, summary.Quality.Status);
            Assert.AreEqual("processing-error.v1", summary.MetricVersion);
            Assert.AreEqual(DataSource.Synthetic, summary.DataSource);
            Assert.AreEqual(StrainLevel.Unavailable, summary.StrainIndicator.Level);
            Assert.IsNull(summary.StrainIndicator.Value);
            foreach (MetricName metric in Enum.GetValues(typeof(MetricName)))
            { Assert.IsNull(summary.Metrics.Get(metric)); Assert.IsNull(summary.Deviations.Get(metric)); }
            StringAssert.Contains("could not be calculated", c.MetricsFailureMessage);
            StringAssert.Contains(c.MetricsFailureMessage, UnityEngine.Object.FindAnyObjectByType<SummaryPanel>().Body.text);
            c.Exit();
            Assert.IsNull(c.MetricsFailureMessage);
        }

        static void RequireBackendRun()
        {
            if (Environment.GetEnvironmentVariable("VR_RUN_BACKEND_TESTS") != "1")
                Assert.Ignore("Set VR_RUN_BACKEND_TESTS=1 with the backend running to test real HTTP.");
        }

        static string ConfiguredUrl(SessionController controller) =>
            Environment.GetEnvironmentVariable("VR_UNITY_BACKEND_TEST_URL") ?? controller.BackendBaseUrl;

        [TestCase("budget_limit", "AI budget limit reached")]
        [TestCase("timeout", "AI request timed out")]
        [TestCase("private-provider-marker", "backend local rules")]
        [TestCase(null, "backend local rules")]
        public void BackendDiagnosticsOnlyDisplayApprovedWording(string code, string description) =>
            Assert.AreEqual(description, HttpAnalysisClient.BackendFallbackDescription(code));

        [UnityTest]
        public IEnumerator InvalidBackendResponseFallsBackWithoutDisplayingItsContents()
        {
            var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            using (var listener = new System.Net.HttpListener())
            {
                var url = $"http://127.0.0.1:{port}";
                listener.Prefixes.Add(url + "/");
                listener.Start();
                var serving = System.Threading.Tasks.Task.Run(async () =>
                {
                    var context = await listener.GetContextAsync();
                    var bytes = System.Text.Encoding.UTF8.GetBytes("{\"private-provider-marker\":true}");
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    context.Response.Close();
                });
                var request = ContractJson.Parse<AnalyzeRequest>(System.IO.File.ReadAllText(
                    System.IO.Path.Combine(Application.dataPath, "..", "..", "fixtures", "analyze_request_synthetic.json")));
                var result = new HttpAnalysisClient(url, "").AnalyzeAsync(request, default);
                yield return WaitUntil(() => result.IsCompleted && serving.IsCompleted, 20);
                Assert.IsFalse(serving.IsFaulted);
                Assert.AreEqual(AiSource.Fallback, result.Result.Response.AiSource);
                Assert.AreEqual("backend response rejected", result.Result.FallbackReason);
                StringAssert.DoesNotContain("private-provider-marker", result.Result.FallbackReason);
            }
        }

        [UnityTest]
        public IEnumerator BackendSessionUsesCurrentAssessmentConfiguration()
        {
            RequireBackendRun();
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            Assert.NotNull(c);
            // The scene's desktop demo default: the synthetic 130 s replay with probes (labelled as replay).
            Assert.AreEqual(App.Capture.EyeSourceMode.Replay, c.EyeSource);
            Assert.AreEqual(AnalysisMode.Http, c.Analysis);
            // Keep the actual scene's durations: 130 + 60 replay seconds at the scene's replay speed.
            c.BackendBaseUrl = ConfiguredUrl(c);
            c.AcceptConsent();
            yield return WaitUntil(() => !c.IsCapturing, c.BaselineSeconds / c.ReplaySpeed + 10);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn, c.TaskSeconds / c.ReplaySpeed + 10);
            c.SubmitCheckIn(new SelfReport
            {
                Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 3,
                Chips = new List<Chip> { Chip.WantQuiet }, FreeText = "private check-in stays here",
            }, new Preferences { AnimalsEnabled = false, Sound = SoundMode.Silent, Motion = MotionMode.None });
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);
            var ctx = c.Flow.Context;
            Assert.AreEqual(AiSource.Fallback, ctx.Analysis.AiSource);
            Assert.AreEqual("cloud use disabled for this check-in", ctx.AnalysisFallbackReason,
                "must receive the backend's local_only header, rather than a Unity transport fallback");
            Assert.AreEqual(ctx.SessionId, ctx.Analysis.SessionId);
            Assert.AreEqual(DataSource.Replay, ctx.Analysis.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, ctx.Analysis.ReplayOrigin);
            Assert.AreNotEqual(StrainLevel.Unavailable, ctx.Summary.StrainIndicator.Level, "the demo replay has probes in both phases");
            var bar = UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>();
            StringAssert.Contains("REPLAY of synthetic data", bar.DataLabel.text);
            // The backend's rules answered (local_only header above), so the label names the backend, not Unity.
            StringAssert.Contains("FALLBACK from backend rules", bar.AiLabel.text);
            Assert.IsFalse(ctx.Analysis.SceneConfig.AnimalsEnabled);
            Assert.AreEqual(SoundMode.Silent, ctx.Analysis.SceneConfig.Sound);
            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            c.FinishScene();
            c.SubmitReRating(2, 2);
            Assert.AreEqual(FlowState.Done, c.Flow.State);
            c.Exit();
            Assert.IsNull(c.Flow.Context.Summary);
        }

        [UnityTest]
        public IEnumerator BackendAuthenticationAndSemanticValidationUseNativeHttp()
        {
            RequireBackendRun();
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            var request = ContractJson.Parse<AnalyzeRequest>(System.IO.File.ReadAllText(
                System.IO.Path.Combine(Application.dataPath, "..", "..", "fixtures", "analyze_request_synthetic.json")));
            var url = ConfiguredUrl(c);
            var token = Environment.GetEnvironmentVariable("VR_BACKEND_ACCESS_TOKEN");
            var valid = new HttpAnalysisClient(url, token ?? "").AnalyzeAsync(request, default);
            yield return WaitUntil(() => valid.IsCompleted, 20);
            Assert.AreEqual("cloud use disabled for this check-in", valid.Result.FallbackReason);
            if (!string.IsNullOrEmpty(token))
            {
                foreach (var badToken in new[] { "", "deliberately-wrong-test-token" })
                {
                    var bad = new HttpAnalysisClient(url, badToken).AnalyzeAsync(request, default);
                    yield return WaitUntil(() => bad.IsCompleted, 20);
                    Assert.AreEqual("backend HTTP 401", bad.Result.FallbackReason);
                }
            }
            // C# type-valid but violates the backend's duration-total semantic rule.
            request.SessionSummary.DurationMs += 1;
            var invalid = new HttpAnalysisClient(url, token ?? "").AnalyzeAsync(request, default);
            yield return WaitUntil(() => invalid.IsCompleted, 20);
            Assert.AreEqual("backend HTTP 422", invalid.Result.FallbackReason);
        }

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
            c.EyeSource = EyeSourceMode.Synthetic;
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
            StringAssert.Contains("Forest scene loaded", c.SceneStatus, "ForestDemo must be in Build Settings");
            Assert.NotNull(UnityEngine.Object.FindAnyObjectByType<App.Relaxation.ForestSceneApplier>());
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);

            c.Exit();
            Assert.AreEqual(FlowState.Exited, c.Flow.State);
            Assert.IsNull(c.Flow.Context.Summary);
        }

        /// <summary>
        /// End-to-end with the real backend in offline (mock) mode. Set VR_SMOKE_BACKEND_URL=http://127.0.0.1:8000
        /// and start the backend first; otherwise the test is ignored.
        /// </summary>
        [UnityTest]
        public IEnumerator SessionThroughRunningBackendReachesForest()
        {
            var url = Environment.GetEnvironmentVariable("VR_SMOKE_BACKEND_URL");
            if (string.IsNullOrEmpty(url)) Assert.Ignore("VR_SMOKE_BACKEND_URL not set (backend not running for this run)");
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.EyeSource = EyeSourceMode.Synthetic;
            c.BaselineSeconds = 2;
            c.TaskSeconds = 2;
            c.Analysis = AnalysisMode.Http;
            c.BackendBaseUrl = url;

            c.AcceptConsent();
            yield return WaitUntil(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn);
            c.SubmitCheckIn(new SelfReport
            {
                Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 5,
                Chips = new List<Chip> { Chip.TiredEyes, Chip.WantQuiet }, FreeText = "private text stays local",
            }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var a = c.Flow.Context.Analysis;
            StringAssert.StartsWith("local-", a.PromptVersion, "the backend's offline rules must have answered: " + c.Flow.Context.AnalysisFallbackReason);
            Assert.AreEqual(AiSource.Fallback, a.AiSource);
            Assert.AreEqual(DataSource.Synthetic, a.DataSource);
            StringAssert.Contains("backend rules", UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>().AiLabel.text);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            StringAssert.Contains("Forest scene loaded", c.SceneStatus);
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);
        }

        /// <summary>Desktop demo default: the synthetic 130 s replay with probes, played fast, offline, to the forest.</summary>
        [UnityTest]
        public IEnumerator DemoReplayShowsDeviationsAndReachesForest()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            Assert.AreEqual(EyeSourceMode.Replay, c.EyeSource, "desktop demo default");
            c.ReplaySpeed = 40;
            c.Analysis = AnalysisMode.Local;

            c.AcceptConsent();
            StringAssert.Contains("REPLAY of synthetic data", UnityEngine.Object.FindAnyObjectByType<ProvenanceBar>().DataLabel.text);
            yield return WaitUntil(() => c.ProbeRunner.Visible, 5);
            yield return WaitUntil(() => !c.IsCapturing, 20);
            c.SubmitBaseline(2, 2);
            yield return WaitUntil(() => c.Flow.State == FlowState.CheckIn, 20);
            c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 5, MentalFatigue = 4, Chips = new List<Chip> { Chip.TiredEyes } }, null);
            yield return WaitUntil(() => c.Flow.Context.Analysis != null, 20);

            var s = c.Flow.Context.Summary;
            Assert.AreEqual(ReplayOrigin.Synthetic, s.ReplayOrigin);
            Assert.IsNull(c.MetricsFailureMessage);
            Assert.AreEqual(QualityStatus.Usable, s.Quality.Status);
            Assert.AreEqual(190000, s.DurationMs, "loading time and render-frame overshoot are outside the two capture phases");
            Assert.IsNotNull(s.Metrics.ProbeLatencyMs);
            Assert.IsNotNull(s.Deviations.ProbeLatencyMs);
            Assert.AreNotEqual(StrainLevel.Unavailable, s.StrainIndicator.Level, "the demo must show the experimental indicator");
            StringAssert.Contains("Probe trials", c.ProbeStatsText);

            c.EnterScene();
            yield return WaitUntil(() => c.SceneStatus.Contains("brightness"));
            StringAssert.Contains("Forest scene loaded", c.SceneStatus);
            c.FinishScene();
            c.SubmitReRating(3, 3);
            Assert.AreEqual(FlowState.Done, c.Flow.State);
        }
    }
}
