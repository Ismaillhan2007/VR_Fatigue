using System;
using System.Linq;
using System.Net;
using App.Core.Analysis;
using App.Core.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace App.Reports
{
    /// <summary>Runtime-only configuration. Never serialise a token or persist participant transfer consent.</summary>
    public sealed class BackendSettings
    {
        public string BaseUrl { get; private set; }
        public string AccessToken { get; private set; }
        public bool IsLoopback { get; private set; }

        public static string InitialUrl(string sceneUrl, string environmentUrl, string launchUrl, bool desktop) =>
            desktop ? First(launchUrl, environmentUrl, sceneUrl) : sceneUrl;

        static string First(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        public static bool TryCreate(string url, string token, bool localDevelopment,
            out BackendSettings settings, out string error)
        {
            settings = null;
            error = "backend settings invalid: use an HTTPS origin without credentials, path, query or fragment";
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            var loopback = uri.IsLoopback;
            var local = loopback;
            if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip))
            {
                var bytes = ip.GetAddressBytes();
                local |= bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) // RFC 6598 shared Wi-Fi/hotspot space.
                    || (bytes[0] == 192 && bytes[1] == 168));
            }
            if ((uri.Scheme != "https" || loopback) && (!localDevelopment || !local))
            {
                error = "backend settings invalid: local HTTP/loopback requires explicit local development mode";
                return false;
            }
            token = token ?? "";
            if (token.Length > 4096 || token.Any(c => c <= 32 || c >= 127))
            {
                error = "backend settings invalid: enter a bearer token without spaces or line breaks";
                return false;
            }
            settings = new BackendSettings { BaseUrl = uri.GetLeftPart(UriPartial.Authority), AccessToken = token, IsLoopback = loopback };
            error = null;
            return true;
        }

        public string TransferBlockReason(AnalyzeRequest request, bool inventedSession, bool allowUpload, bool consentedSummary = false)
        {
            if (request.CloudPolicy == CloudPolicy.ConsentedSummary)
                return consentedSummary ? null : "cloud summary consent missing: using on-device report";
            if (!allowUpload) return "backend upload not permitted: using on-device report";
            var s = request.SessionSummary;
            if (!inventedSession || !(s.DataSource == DataSource.Synthetic
                || (s.DataSource == DataSource.Replay && s.ReplayOrigin == ReplayOrigin.Synthetic)))
                return "backend participant transfer disabled: use wholly invented demo data";
            return null;
        }
    }

    /// <summary>Client-side transfer boundary; local_only controls Gemini, not backend upload.</summary>
    public sealed class ConfiguredBackendClient : IAnalysisClient
    {
        readonly BackendSettings settings;
        readonly string invalidReason;
        readonly bool invented, allowUpload, consentedSummary;

        public ConfiguredBackendClient(string url, string token, bool localDevelopment, bool invented, bool allowUpload, bool consentedSummary = false)
        {
            BackendSettings.TryCreate(url, token, localDevelopment, out settings, out invalidReason);
            this.invented = invented;
            this.allowUpload = allowUpload;
            this.consentedSummary = consentedSummary;
        }

        public string Name => "configured backend";

        public Task<AnalysisResult> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
        {
            var reason = invalidReason ?? settings.TransferBlockReason(request, invented, allowUpload, consentedSummary);
            return reason == null
                ? new HttpAnalysisClient(settings.BaseUrl, settings.AccessToken).AnalyzeAsync(request, cancellationToken)
                : Task.FromResult(new AnalysisResult { Response = LocalFallbackAnalysis.Analyze(request), FallbackReason = reason });
        }
    }
}
