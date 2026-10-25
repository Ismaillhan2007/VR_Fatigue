using App.Core.Contracts;
using Newtonsoft.Json;

namespace App.Core.Analysis
{
    /// <summary>
    /// contracts/v1/analyze_request.schema.json: body of POST /api/v1/analyze. Reconciliation and safety_notice are
    /// computed locally and are authoritative; the backend may not change them (safety may only be raised).
    /// Free text never leaves the device (CONCEPT decision D1 default): self_report.free_text and preferences.text are null.
    /// </summary>
    public sealed class AnalyzeRequest : IContract
    {
        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        /// <summary>Default is local-only. ConsentedSummary requires a separate participant choice; SyntheticDemo is invented data only.</summary>
        [JsonProperty("cloud_policy", Required = Required.DisallowNull)] public CloudPolicy CloudPolicy = CloudPolicy.LocalOnly;
        [JsonProperty("session_summary", Required = Required.Always)] public SessionSummary SessionSummary;
        /// <summary>Null when the participant let the app choose.</summary>
        [JsonProperty("preferences", Required = Required.AllowNull)] public Preferences Preferences;
        [JsonProperty("reconciliation", Required = Required.Always)] public Reconciliation Reconciliation;
        [JsonProperty("safety_notice", Required = Required.Always)] public SafetyNotice SafetyNotice;

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Defined(path + ".cloud_policy", CloudPolicy);
            p.Nested(path + ".session_summary", SessionSummary);
            if (Preferences != null) Preferences.Validate(p, path + ".preferences");
            p.Defined(path + ".reconciliation", Reconciliation);
            p.Defined(path + ".safety_notice", SafetyNotice);
            if (SessionSummary?.SelfReport?.FreeText != null)
                p.Add(path + ".session_summary.self_report.free_text", "must be null in a request (free text stays local)");
            if (Preferences?.Text != null) p.Add(path + ".preferences.text", "must be null in a request (free text stays local)");
        }

        /// <summary>
        /// Builds a request with the deterministic reconciliation and safety rules applied to the full local data,
        /// then removes free text from copies. The caller's objects are not modified.
        /// </summary>
        public static AnalyzeRequest Create(SessionSummary summary, SelfReport baselineReport, Preferences preferences)
        {
            var post = summary.SelfReport;
            var wireSummary = ContractJson.Parse<SessionSummary>(ContractJson.Serialize(summary));
            if (wireSummary.SelfReport != null) wireSummary.SelfReport.FreeText = null;
            Preferences wirePreferences = null;
            if (preferences != null)
            {
                wirePreferences = ContractJson.Parse<Preferences>(ContractJson.Serialize(preferences));
                wirePreferences.Text = null;
            }
            return new AnalyzeRequest
            {
                CloudPolicy = CloudPolicy.LocalOnly,
                SessionSummary = wireSummary,
                Preferences = wirePreferences,
                Reconciliation = ReconciliationRules.Compute(post, baselineReport, summary.StrainIndicator.Level),
                SafetyNotice = SafetyRules.Evaluate(post),
            };
        }
    }
}
