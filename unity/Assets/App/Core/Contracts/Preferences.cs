using Newtonsoft.Json;

namespace App.Core.Contracts
{
    /// <summary>contracts/v1/preferences.schema.json. Explicit participant choices always win over AI or rule output.</summary>
    public sealed class Preferences : IContract
    {
        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        [JsonProperty("theme", Required = Required.Always)] public Theme Theme = Theme.Forest;
        [JsonProperty("animals_enabled", Required = Required.Always)] public bool AnimalsEnabled = true;
        [JsonProperty("time_of_day", Required = Required.Always)] public TimeOfDay TimeOfDay = TimeOfDay.Sunset;
        [JsonProperty("sound", Required = Required.Always)] public SoundMode Sound = SoundMode.Forest;
        [JsonProperty("motion", Required = Required.Always)] public MotionMode Motion = MotionMode.Gentle;
        [JsonProperty("text", Required = Required.AllowNull)] public string Text;

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Defined(path + ".theme", Theme);
            p.Defined(path + ".time_of_day", TimeOfDay);
            p.Defined(path + ".sound", Sound);
            p.Defined(path + ".motion", Motion);
            p.MaxLength(path + ".text", Text, 300);
        }
    }
}
