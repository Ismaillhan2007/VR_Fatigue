using App.Core.Analysis;
using App.Core.Contracts;
using App.Reports;
using System.IO;
using NUnit.Framework;

namespace App.Tests
{
    public class BackendSettingsTests
    {
        [Test]
        public void PrivateOperatorSetupLoadsConnectionWithoutGrantingParticipantConsent()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"backend_url\":\"http://100.64.127.140:8147\",\"backend_token\":\"invented-defaults-token\",\"local_development\":true}");
                Assert.IsTrue(OperatorBackendDefaults.TryLoad(path, out var settings, out var status));
                Assert.AreEqual("http://100.64.127.140:8147", settings.BaseUrl);
                Assert.AreEqual("invented-defaults-token", settings.AccessToken);
                StringAssert.DoesNotContain("invented-defaults-token", status);
            }
            finally { File.Delete(path); }
        }

        [TestCase("{}")]
        [TestCase("{\"backend_url\":\"http://100.64.127.140:8147\",\"backend_token\":\"private-marker\",\"local_development\":false}")]
        [TestCase("{\"backend_url\":\"https://example.com\",\"backend_token\":\"private-marker\",\"local_development\":true,\"cloud_consent\":true}")]
        [TestCase("{\"backend_url\":\"https://example.com\",\"backend_token\":\"\",\"local_development\":true}")]
        [TestCase("{\"backend_url\":\"https://example.com\",\"backend_token\":\"private-marker\",\"local_development\":\"true\"}")]
        public void InvalidOperatorSetupCannotEchoCredentialsOrGrantCloudConsent(string json)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, json);
                Assert.IsFalse(OperatorBackendDefaults.TryLoad(path, out _, out var status));
                StringAssert.DoesNotContain("private-marker", status);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ConsentedSummaryContractRoundTripsAndNeedsSeparateParticipantChoice()
        {
            var request = TestFixtures.Load<AnalyzeRequest>("analyze_request_consented_summary.json");
            CollectionAssert.IsEmpty(request.Validate());
            Assert.AreEqual(CloudPolicy.ConsentedSummary, ContractJson.Parse<AnalyzeRequest>(ContractJson.Serialize(request)).CloudPolicy);
            Assert.IsTrue(BackendSettings.TryCreate("https://example.com", "", false, out var settings, out _));
            request.SessionSummary.DataSource = DataSource.Live;
            Assert.NotNull(settings.TransferBlockReason(request, true, true));
            Assert.IsNull(settings.TransferBlockReason(request, false, false, consentedSummary: true));
            request.CloudPolicy = CloudPolicy.LocalOnly;
            Assert.NotNull(settings.TransferBlockReason(request, false, false, consentedSummary: true),
                "request policy and participant choice must both opt in");
        }

        [TestCase("https://example.com/", false, "https://example.com")]
        [TestCase("  https://example.com:8443/  ", false, "https://example.com:8443")]
        [TestCase("http://127.0.0.1:8000/", true, "http://127.0.0.1:8000")]
        [TestCase("http://localhost:8000", true, "http://localhost:8000")]
        [TestCase("http://[::1]:8000", true, "http://[::1]:8000")]
        [TestCase("http://10.0.0.1:8000", true, "http://10.0.0.1:8000")]
        [TestCase("http://172.16.0.1:8000", true, "http://172.16.0.1:8000")]
        [TestCase("http://192.168.1.2:8000", true, "http://192.168.1.2:8000")]
        [TestCase("http://100.64.127.140:8147", true, "http://100.64.127.140:8147")]
        [TestCase("http://100.127.255.254:8147", true, "http://100.127.255.254:8147")]
        public void AcceptsAndNormalizesExplicitOrigins(string url, bool local, string expected)
        {
            Assert.IsTrue(BackendSettings.TryCreate(url, "invented-token", local, out var s, out var error), error);
            Assert.AreEqual(expected, s.BaseUrl);
            Assert.AreEqual("invented-token", s.AccessToken);
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("not-a-url", false)]
        [TestCase("ftp://example.com", true)]
        [TestCase("http://example.com", true)]
        [TestCase("http://8.8.8.8", true)]
        [TestCase("http://172.32.0.1", true)]
        [TestCase("http://100.63.255.255", true)]
        [TestCase("http://100.128.0.1", true)]
        [TestCase("http://100.64.127.140:8147", false)]
        [TestCase("http://127.0.0.1:8000", false)]
        [TestCase("https://localhost:8000", false)]
        [TestCase("https://user:private-marker@example.com", false)]
        [TestCase("https://example.com?token=private-marker", false)]
        [TestCase("https://example.com#private-marker", false)]
        [TestCase("https://example.com/api", false)]
        public void RejectsUnsafeSettingsWithoutEchoingInput(string url, bool local)
        {
            Assert.IsFalse(BackendSettings.TryCreate(url, "", local, out var s, out var error));
            Assert.IsNull(s);
            StringAssert.DoesNotContain("private-marker", error);
        }

        [TestCase("token with spaces")]
        [TestCase("private-marker\r\nInjected: true")]
        [TestCase("unicode-ä")]
        public void RejectsInvalidBearerTokensWithoutEcho(string token)
        {
            Assert.IsFalse(BackendSettings.TryCreate("https://example.com", token, false, out _, out var error));
            StringAssert.DoesNotContain(token, error);
        }

        [Test]
        public void DesktopLaunchOverridesEnvironmentAndAndroidIgnoresBoth()
        {
            Assert.AreEqual("launch", BackendSettings.InitialUrl("scene", "environment", "launch", true));
            Assert.AreEqual("environment", BackendSettings.InitialUrl("scene", "environment", null, true));
            Assert.AreEqual("scene", BackendSettings.InitialUrl("scene", null, " ", true));
            Assert.AreEqual("scene", BackendSettings.InitialUrl("scene", "environment", "launch", false));
        }

        [TestCase("https://example.com", false)]
        [TestCase("http://127.0.0.1:8000", true)]
        public void EvenLoopbackNeedsSeparateInventedSessionAndUploadPermission(string url, bool local)
        {
            Assert.IsTrue(BackendSettings.TryCreate(url, "", local, out var settings, out _));
            var request = TestFixtures.Load<AnalyzeRequest>("analyze_request_synthetic.json");
            Assert.NotNull(settings.TransferBlockReason(request, true, false));
            Assert.NotNull(settings.TransferBlockReason(request, false, true));
            Assert.IsNull(settings.TransferBlockReason(request, true, true));
            request.SessionSummary.DataSource = DataSource.Live;
            Assert.NotNull(settings.TransferBlockReason(request, true, true));
            request.SessionSummary.DataSource = DataSource.Replay;
            request.SessionSummary.ReplayOrigin = ReplayOrigin.Recorded;
            Assert.NotNull(settings.TransferBlockReason(request, true, true));
            request.SessionSummary.ReplayOrigin = ReplayOrigin.Synthetic;
            Assert.IsNull(settings.TransferBlockReason(request, true, true));
        }

        [Test]
        public void PackagedProbeMatchesTheWhollyInventedSourceFixture()
        {
            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("backend-connection-probe");
            Assert.NotNull(asset);
            Assert.AreEqual(TestFixtures.Read("analyze_request_synthetic.json"), asset.text);
        }

        [Test]
        public void TokenAndUploadPermissionAreNotSerializedSceneFields()
        {
            var type = System.Type.GetType("App.UI.SessionController, App.UI", true);
            foreach (var name in new[] { "BackendToken", "AllowBackendUpload", "InventedDemoSession" })
            {
                Assert.IsNull(type.GetField(name));
                var backing = type.GetField("<" + name + ">k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(backing);
                Assert.IsFalse(System.Attribute.IsDefined(backing, typeof(UnityEngine.SerializeField)));
            }
        }

        [Test]
        public void DeclinedTransferReturnsLocalReportWithoutNetworkWork()
        {
            var request = TestFixtures.Load<AnalyzeRequest>("analyze_request_synthetic.json");
            var result = new ConfiguredBackendClient("https://unreachable.invalid", "", false, true, false)
                .AnalyzeAsync(request, default);
            Assert.IsTrue(result.IsCompleted);
            Assert.AreEqual(LocalFallbackAnalysis.PromptVersion, result.Result.Response.PromptVersion);
            Assert.AreEqual(request.SessionSummary.SessionId, result.Result.Response.SessionId);
            StringAssert.Contains("upload not permitted", result.Result.FallbackReason);
        }
    }
}
