using System.Collections.Generic;
using Newtonsoft.Json;

namespace App.Core.Contracts
{
    /// <summary>
    /// contracts/v1/analysis_response.schema.json. Data provenance (data_source, replay_origin) and AI provenance
    /// (ai_source, model_id) are independent fields.
    /// </summary>
    public sealed class AnalysisResponse : IContract
    {
        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        [JsonProperty("session_id", Required = Required.Always)] public string SessionId;
        [JsonProperty("data_source", Required = Required.Always)] public DataSource DataSource;
        [JsonProperty("replay_origin", Required = Required.AllowNull)] public ReplayOrigin? ReplayOrigin;
        [JsonProperty("quality", Required = Required.Always)] public Quality Quality = new Quality();
        /// <summary>Computed by the app (ReconciliationRules); the AI may not change it.</summary>
        [JsonProperty("reconciliation", Required = Required.Always)] public Reconciliation Reconciliation;
        [JsonProperty("observations", Required = Required.Always)] public List<Observation> Observations = new List<Observation>();
        [JsonProperty("narrative", Required = Required.Always)] public string Narrative = "";
        [JsonProperty("limitations", Required = Required.Always)] public List<string> Limitations = new List<string>();
        /// <summary>Set by local rules; the AI may only raise it.</summary>
        [JsonProperty("safety_notice", Required = Required.Always)] public SafetyNotice SafetyNotice;
        [JsonProperty("scene_rationale", Required = Required.Always)] public string SceneRationale = "";
        [JsonProperty("scene_config", Required = Required.Always)] public SceneConfig SceneConfig;
        [JsonProperty("ai_source", Required = Required.Always)] public AiSource AiSource;
        [JsonProperty("model_id", Required = Required.AllowNull)] public string ModelId;
        [JsonProperty("prompt_version", Required = Required.Always)] public string PromptVersion;

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Uuid(path + ".session_id", SessionId);
            p.Defined(path + ".data_source", DataSource);
            p.Defined(path + ".replay_origin", ReplayOrigin);
            p.Nested(path + ".quality", Quality);
            p.Defined(path + ".reconciliation", Reconciliation);
            p.List(path + ".observations", Observations, 0, 6, unique: false);
            if (Observations != null) for (var i = 0; i < Observations.Count; i++) p.Nested($"{path}.observations[{i}]", Observations[i]);
            p.Required(path + ".narrative", Narrative);
            p.MaxLength(path + ".narrative", Narrative, 600);
            p.List(path + ".limitations", Limitations, 1, 5, unique: false);
            if (Limitations != null)
                foreach (var l in Limitations) { p.Required(path + ".limitations[]", l); p.MaxLength(path + ".limitations[]", l, 200); }
            p.Defined(path + ".safety_notice", SafetyNotice);
            p.Required(path + ".scene_rationale", SceneRationale);
            p.MaxLength(path + ".scene_rationale", SceneRationale, 200);
            p.Nested(path + ".scene_config", SceneConfig);
            p.Defined(path + ".ai_source", AiSource);
            p.MaxLength(path + ".model_id", ModelId, 64);
            p.Required(path + ".prompt_version", PromptVersion);
            p.MaxLength(path + ".prompt_version", PromptVersion, 32);

            Provenance.CheckReplayOrigin(p, path, DataSource, ReplayOrigin);
            if (AiSource == AiSource.Fallback && ModelId != null) p.Add(path + ".model_id", "a fallback response never names a model");
        }
    }

    /// <summary>Reference to an approved observation code; numbers stay in SessionSummary.</summary>
    public sealed class Observation : IContract
    {
        [JsonProperty("code", Required = Required.Always)] public ObservationCode Code;
        [JsonProperty("metric", Required = Required.AllowNull)] public MetricName? Metric;

        public void Validate(Problems p, string path)
        {
            p.Defined(path + ".code", Code);
            p.Defined(path + ".metric", Metric);
        }
    }
}
