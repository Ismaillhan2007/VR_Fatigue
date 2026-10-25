using System.Collections.Generic;
using Newtonsoft.Json;

namespace App.Core.Contracts
{
    /// <summary>contracts/v1/self_report.schema.json. Self-report is not a sensor measurement.</summary>
    public sealed class SelfReport : IContract
    {
        public const int MaxFreeText = 300;

        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        [JsonProperty("phase", Required = Required.Always)] public ReportPhase Phase;
        [JsonProperty("eye_strain", Required = Required.Always)] public int EyeStrain = 1;
        [JsonProperty("mental_fatigue", Required = Required.Always)] public int MentalFatigue = 1;
        [JsonProperty("chips", Required = Required.Always)] public List<Chip> Chips = new List<Chip>();
        /// <summary>Untrusted user text. Not sent to a cloud model by default (CONCEPT decision D1).</summary>
        [JsonProperty("free_text", Required = Required.AllowNull)] public string FreeText;

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Defined(path + ".phase", Phase);
            p.Range(path + ".eye_strain", EyeStrain, 1, 7);
            p.Range(path + ".mental_fatigue", MentalFatigue, 1, 7);
            p.List(path + ".chips", Chips, 0, 6, unique: true);
            if (Chips != null) foreach (var c in Chips) p.Defined(path + ".chips[]", c);
            p.MaxLength(path + ".free_text", FreeText, MaxFreeText);
        }

        public SelfReport Copy() => new SelfReport
        {
            SchemaVersion = SchemaVersion, Phase = Phase, EyeStrain = EyeStrain, MentalFatigue = MentalFatigue,
            Chips = Chips == null ? null : new List<Chip>(Chips), FreeText = FreeText,
        };
    }
}
