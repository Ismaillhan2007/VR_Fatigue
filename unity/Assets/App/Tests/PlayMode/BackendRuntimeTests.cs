using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using App.Capture;
using App.Core.Analysis;
using App.Core.Contracts;
using App.Core.Flow;
using App.Reports;
using App.UI;
using App.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace App.Tests
{
    public class BackendRuntimeTests
    {
        [UnityTest]
        public IEnumerator NewSessionRetainsOperatorConnectionButNeverParticipantConsentAndExitClearsToken()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.ConfigureBackend("http://100.64.127.140:8147", "invented-operator-token", true, true, true);
            c.Analysis = AnalysisMode.Local;
            c.Flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities(), "invented new session test");
            c.Flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
            c.Flow.CompleteLoadTask();
            c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 4 }, null);
            c.FinishWithReport(); c.Restart();
            Assert.AreEqual("invented-operator-token", c.BackendToken);
            Assert.IsTrue(c.BackendLocalDevelopment);
            Assert.IsFalse(c.InventedDemoSession); Assert.IsFalse(c.AllowBackendUpload);
            Assert.IsNull(c.Flow.Context.Summary); Assert.IsNull(c.Flow.Context.Analysis);
            c.Flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities(), "invented next participant");
            c.Flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
            c.Flow.CompleteLoadTask();
            Assert.IsFalse(c.Panels.OfType<CheckInPanel>().First().CloudExplanation.isOn);
            c.Exit();
            Assert.IsEmpty(c.BackendToken);
        }

        [UnityTest]
        public IEnumerator CompletedReplayCanReceiveLiveGeminiBeforeFinishing()
        {
            var url = Environment.GetEnvironmentVariable("VR_GEMINI_BACKEND_TEST_URL");
            if (string.IsNullOrEmpty(url)) Assert.Ignore("Manual budgeted Gemini verification only");
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.ConfigureBackend(url, Environment.GetEnvironmentVariable("VR_GEMINI_BACKEND_TEST_TOKEN"), true, false, false);
            c.ReplaySpeed = 40;
            c.AcceptConsent();
            yield return WaitFor(() => !c.IsCapturing);
            c.SubmitBaseline(2, 2);
            yield return WaitFor(() => c.Flow.State == FlowState.CheckIn);
            var checkIn = c.Panels.OfType<CheckInPanel>().First();
            checkIn.EyeStrain.Slider.value = 4; checkIn.MentalFatigue.Slider.value = 4;
            checkIn.CloudExplanation.isOn = true;
            checkIn.SubmitButton.onClick.Invoke();
            yield return WaitFor(() => !c.AnalysisInProgress);
            Assert.IsNull(c.MetricsFailureMessage);
            Assert.AreEqual(AiSource.Live, c.Flow.Context.Analysis.AiSource, c.Flow.Context.AnalysisFallbackReason);
            Assert.AreEqual(DataSource.Replay, c.Flow.Context.Analysis.DataSource);
            Assert.AreEqual(ReplayOrigin.Synthetic, c.Flow.Context.Analysis.ReplayOrigin);
            Assert.AreEqual(190000, c.Flow.Context.Summary.DurationMs);
            StringAssert.Contains("AI: live", c.ProvenanceBar.AiLabel.text);
            yield return AssessmentFlowSmokeTest.CaptureEvidence("completed-gemini-report");
            c.FinishWithReport();
            Assert.AreEqual(FlowState.Done, c.Flow.State);
            Assert.AreEqual(AiSource.Live, c.Flow.Context.Analysis.AiSource);
            c.Exit();
        }

        [UnityTest]
        public IEnumerator LocalReportCanOptIntoGeminiWithoutRepeatingCaptureOrSendingFreeText()
        {
            using (var receiver = new Receiver(wait: true))
            {
                yield return SceneManager.LoadSceneAsync("Assessment");
                var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
                c.Analysis = AnalysisMode.Local;
                c.ConfigureBackend(receiver.Url, "invented-retry-token", true, false, false);
                c.Flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities(), "invented retry test");
                c.Flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
                c.Flow.CompleteLoadTask();
                c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 4, FreeText = "private-retry-marker" }, null);
                var summary = c.Panels.OfType<SummaryPanel>().First();
                Assert.IsFalse(summary.CloudExplanation.isOn);
                Assert.IsFalse(summary.GenerateGeminiButton.interactable);
                c.RequestGeminiExplanation(false);
                Assert.IsFalse(receiver.Received.Task.IsCompleted);
                var id = c.Flow.Context.SessionId;
                summary.CloudExplanation.isOn = true;
                Assert.IsTrue(summary.GenerateGeminiButton.interactable);
                summary.GenerateGeminiButton.onClick.Invoke();
                Assert.IsFalse(summary.GenerateGeminiButton.interactable);
                c.RequestGeminiExplanation(true); // A double click cannot start a duplicate request.
                yield return WaitFor(() => receiver.Received.Task.IsCompleted);
                var request = ContractJson.Parse<AnalyzeRequest>(receiver.Body);
                Assert.AreEqual(id, request.SessionSummary.SessionId);
                Assert.AreEqual(CloudPolicy.ConsentedSummary, request.CloudPolicy);
                Assert.IsNull(request.SessionSummary.SelfReport.FreeText);
                Assert.AreEqual(4, request.SessionSummary.SelfReport.MentalFatigue);
                StringAssert.Contains("cancel Gemini", summary.FinishReportButton.GetComponentInChildren<Text>().text);
                yield return AssessmentFlowSmokeTest.CaptureEvidence("report-gemini-retry");
                receiver.Release.TrySetResult(true);
                yield return WaitFor(() => !c.AnalysisInProgress);
                Assert.AreEqual(FlowState.Summary, c.Flow.State);
                c.FinishWithReport();
                Assert.AreEqual(FlowState.Done, c.Flow.State);
                c.Exit();
            }
        }

        [UnityTest]
        public IEnumerator ExplicitLiveGeminiRoundTripUsesOnlyTheInventedProbe()
        {
            var url = Environment.GetEnvironmentVariable("VR_GEMINI_BACKEND_TEST_URL");
            if (string.IsNullOrEmpty(url)) Assert.Ignore("Manual budgeted Gemini verification only");
            var request = Request();
            request.CloudPolicy = CloudPolicy.ConsentedSummary;
            var work = new HttpAnalysisClient(url, Environment.GetEnvironmentVariable("VR_GEMINI_BACKEND_TEST_TOKEN"))
                .AnalyzeAsync(request, default);
            yield return WaitFor(() => work.IsCompleted);
            Assert.AreEqual(AiSource.Live, work.Result.Response.AiSource, work.Result.FallbackReason);
            Assert.IsNotEmpty(work.Result.Response.ModelId);
            Assert.AreEqual(request.SessionSummary.DataSource, work.Result.Response.DataSource);
            Assert.AreEqual(request.SessionSummary.SessionId, work.Result.Response.SessionId);
        }

        [UnityTest]
        public IEnumerator ConsentedCloudRequestNeverBlocksLocalReportAndFinishCancelsPendingWork()
        {
            using (var receiver = new Receiver(wait: true))
            {
                yield return SceneManager.LoadSceneAsync("Assessment");
                var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
                c.ConfigureBackend(receiver.Url, "invented-consent-token", true, false, false);
                c.Flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities(), "invented policy test");
                c.Flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
                c.Flow.CompleteLoadTask();
                var checkIn = c.Panels.Find(p => p is CheckInPanel) as CheckInPanel;
                Assert.IsFalse(checkIn.CloudExplanation.isOn);
                checkIn.EyeStrain.Slider.value = 3; checkIn.MentalFatigue.Slider.value = 4;
                checkIn.FreeText.text = "invented-private-marker";
                checkIn.CloudExplanation.isOn = true;
                yield return AssessmentFlowSmokeTest.CaptureEvidence("gemini-consent");
                checkIn.SubmitButton.onClick.Invoke();
                yield return WaitFor(() => receiver.Received.Task.IsCompleted);
                Assert.IsTrue(c.AnalysisInProgress);
                Assert.NotNull(c.Flow.Context.Analysis, "report exists before a network response");
                var request = ContractJson.Parse<AnalyzeRequest>(receiver.Body);
                Assert.AreEqual(CloudPolicy.ConsentedSummary, request.CloudPolicy);
                Assert.IsNull(request.SessionSummary.SelfReport.FreeText);
                var summary = c.Panels.Find(p => p is SummaryPanel) as SummaryPanel;
                Assert.IsTrue(summary.FinishReportButton.interactable);
                StringAssert.Contains("Report ready on-device", summary.Body.text);
                summary.FinishReportButton.onClick.Invoke();
                Assert.AreEqual(FlowState.Done, c.Flow.State);
                Assert.IsFalse(c.AnalysisInProgress);
                Assert.IsFalse(SceneManager.GetSceneByName("ForestDemo").isLoaded);
                receiver.Release.TrySetResult(true);
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(FlowState.Done, c.Flow.State);
                Assert.AreEqual(LocalFallbackAnalysis.PromptVersion, c.Flow.Context.Analysis.PromptVersion);
                c.Exit(); c.Restart();
                c.Flow.AcceptConsent(DataSource.Synthetic, null, new Capabilities(), "invented reset test");
                c.Flow.CompleteBaseline(new SelfReport { Phase = ReportPhase.Baseline, EyeStrain = 2, MentalFatigue = 2 });
                c.Flow.CompleteLoadTask();
                Assert.IsFalse(checkIn.CloudExplanation.isOn, "cloud choice is never retained into another session");
                c.Exit();
            }
        }

        static AnalyzeRequest Request() => ContractJson.Parse<AnalyzeRequest>(Resources.Load<TextAsset>("backend-connection-probe").text);
        static IEnumerator WaitFor(Func<bool> ready, float seconds = 20)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (!ready() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(ready(), "timed out");
        }

        internal sealed class Receiver : IDisposable
        {
            readonly HttpListener listener = new HttpListener();
            public readonly TaskCompletionSource<bool> Received = new TaskCompletionSource<bool>();
            public readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>();
            public string Url;
            public string Authorization, Body, Path;
            public Receiver(int status = 200, bool wait = false, bool invalid = false, bool redirect = false)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
                Url = "http://127.0.0.1:" + port;
                listener.Prefixes.Add(Url + "/"); listener.Start();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var context = await listener.GetContextAsync();
                        Authorization = context.Request.Headers["Authorization"];
                        Path = context.Request.Url.AbsolutePath;
                        using (var reader = new StreamReader(context.Request.InputStream)) Body = await reader.ReadToEndAsync();
                        Received.TrySetResult(true);
                        if (wait) await Release.Task;
                        context.Response.StatusCode = status;
                        if (redirect) context.Response.RedirectLocation = Url + "/unexpected-target";
                        string body = "private-server-marker";
                        if (status == 200 && !invalid)
                        {
                            var response = LocalFallbackAnalysis.Analyze(ContractJson.Parse<AnalyzeRequest>(Body));
                            response.PromptVersion = "backend-rules-test";
                            body = ContractJson.Serialize(response);
                            context.Response.Headers["X-VR-Fallback-Reason"] = "local_only";
                        }
                        var bytes = Encoding.UTF8.GetBytes(body);
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        context.Response.Close();
                    }
                    catch (HttpListenerException) { }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                });
            }
            public void Dispose() { Release.TrySetResult(true); listener.Close(); }
        }

        [UnityTest]
        public IEnumerator ProbeVerifiesAnalyzeAuthContractWithoutUploadingTheSession()
        {
            using (var receiver = new Receiver())
            {
                yield return SceneManager.LoadSceneAsync("Assessment");
                var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
                var panel = UnityEngine.Object.FindAnyObjectByType<ConsentPanel>();
                panel.OpenSettingsButton.onClick.Invoke();
                Assert.IsTrue(panel.SettingsRoot.activeSelf);
                Assert.AreEqual(InputField.ContentType.Password, panel.BackendToken.contentType);
                Assert.IsTrue(panel.BackendToken.shouldHideMobileInput);
                panel.BackendUrl.text = receiver.Url;
                panel.BackendToken.text = "invented-runtime-token";
                panel.LocalDevelopment.isOn = true;
                panel.InventedDemo.isOn = false;
                panel.AllowUpload.isOn = false;
                panel.CheckConnectionButton.onClick.Invoke();
                Assert.IsFalse(panel.CheckConnectionButton.interactable);
                yield return WaitFor(() => !c.BackendCheckInProgress);
                StringAssert.Contains("Connected: analyze/auth/contract accepted", c.BackendConnectionStatus);
                yield return AssessmentFlowSmokeTest.CaptureEvidence("backend-settings");
                Assert.AreEqual("/api/v1/analyze", receiver.Path);
                Assert.AreEqual("Bearer invented-runtime-token", receiver.Authorization);
                var request = ContractJson.Parse<AnalyzeRequest>(receiver.Body);
                Assert.AreEqual(CloudPolicy.LocalOnly, request.CloudPolicy);
                Assert.AreEqual(DataSource.Synthetic, request.SessionSummary.DataSource);
                Assert.IsNull(request.SessionSummary.SelfReport.FreeText);
                Assert.AreEqual(FlowState.Consent, c.Flow.State);
                Assert.IsNull(c.Flow.Context.Summary);
                Assert.IsFalse(c.AllowBackendUpload);
                panel.SaveSettingsButton.onClick.Invoke();
                Assert.IsFalse(panel.SettingsRoot.activeSelf);
                c.Exit(); c.Restart();
                Assert.IsEmpty(c.BackendToken);
                Assert.IsEmpty(panel.BackendToken.text);
                Assert.IsFalse(c.AllowBackendUpload);
                Assert.IsFalse(c.InventedDemoSession);
            }
        }

        [UnityTest]
        public IEnumerator NativeHttpRejectsAuthSchemaAndRedirectWithoutExposingBody()
        {
            foreach (var status in new[] { 401, 403, 422, 302, 200 })
            using (var receiver = new Receiver(status, invalid: status == 200, redirect: status == 302))
            {
                var client = new HttpAnalysisClient(receiver.Url, "invented-header-token");
                using (var wire = client.CreateRequest(Request())) Assert.AreEqual(0, wire.redirectLimit);
                var result = client.AnalyzeAsync(Request(), default);
                yield return WaitFor(() => result.IsCompleted);
                Assert.IsFalse(client.BackendResponseAccepted);
                Assert.AreEqual(AiSource.Fallback, result.Result.Response.AiSource);
                Assert.AreEqual(status == 200 ? "backend response rejected" : "backend HTTP " + status, result.Result.FallbackReason);
                StringAssert.DoesNotContain("private-server-marker", result.Result.FallbackReason);
                StringAssert.DoesNotContain("invented-header-token", client.Name);
            }
        }

        [UnityTest]
        public IEnumerator PendingConnectionCheckCannotRestoreStatusOrTokenAfterReset()
        {
            using (var receiver = new Receiver(wait: true))
            {
                yield return SceneManager.LoadSceneAsync("Assessment");
                var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
                c.ConfigureBackend(receiver.Url, "invented-runtime-token", true, true, true);
                c.CheckBackendConnection();
                yield return WaitFor(() => receiver.Received.Task.IsCompleted);
                c.Exit(); c.Restart();
                var status = c.BackendConnectionStatus;
                receiver.Release.TrySetResult(true);
                for (var i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(status, c.BackendConnectionStatus);
                Assert.IsEmpty(c.BackendToken);
                Assert.AreEqual(FlowState.Consent, c.Flow.State);
                Assert.IsNull(c.Flow.Context.Analysis);
                Assert.IsFalse(c.BackendCheckInProgress);
            }
        }

        [UnityTest]
        public IEnumerator TimeoutKeepsLocalFallbackAndRealProvenance()
        {
            using (var receiver = new Receiver(wait: true))
            {
                var request = Request();
                var client = new HttpAnalysisClient(receiver.Url, "");
                var result = client.AnalyzeAsync(request, default);
                yield return WaitFor(() => result.IsCompleted, 22);
                Assert.IsFalse(client.BackendResponseAccepted);
                Assert.AreEqual("backend connection failed or timed out", result.Result.FallbackReason);
                Assert.AreEqual(request.SessionSummary.DataSource, result.Result.Response.DataSource);
                Assert.AreEqual(request.SessionSummary.SessionId, result.Result.Response.SessionId);
            }
        }

        [UnityTest]
        public IEnumerator MissingUploadPermissionCompletesReportOnDevice()
        {
            yield return SceneManager.LoadSceneAsync("Assessment");
            var c = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            c.ConfigureBackend("https://unreachable.invalid", "invented-token", false, false, false);
            c.EyeSource = EyeSourceMode.Synthetic;
            c.AcceptConsent(); c.EndCaptureEarly();
            c.SubmitBaseline(2, 2); c.EndCaptureEarly();
            c.SubmitCheckIn(new SelfReport { Phase = ReportPhase.PostTask, EyeStrain = 3, MentalFatigue = 3 }, null);
            yield return WaitFor(() => c.Flow.Context.Analysis != null && !c.AnalysisInProgress);
            StringAssert.Contains("upload not permitted", c.Flow.Context.AnalysisFallbackReason);
            StringAssert.Contains("LOCAL FALLBACK", c.ProvenanceBar.AiLabel.text);
            c.FinishWithReport();
            Assert.AreEqual(FlowState.Done, c.Flow.State);
            Assert.IsFalse(SceneManager.GetSceneByName("ForestDemo").isLoaded);
            c.Exit();
        }
    }
}
