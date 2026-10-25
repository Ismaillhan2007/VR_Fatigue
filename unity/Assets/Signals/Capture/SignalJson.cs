using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRFatigue.Signals
{
    // Strict wire boundary. Core calculations do not depend on JSON or UnityEngine.
    public static class SignalJson
    {
        public static EyeSession ParseSession(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            try
            {
                JObject root = ReadObject(json);
                var obj = Fields(root, "schema_version", "session_id", "data_source", "replay_origin", "coordinate_frame", "start_timestamp_us", "end_timestamp_us", "capabilities", "samples", "probe_events");
                if (Text(obj["schema_version"]) != SessionInfo.SchemaVersion) throw new FormatException("Unsupported schema_version.");
                if (Text(obj["coordinate_frame"]) != "head_local") throw new FormatException("Only head_local gaze directions are supported.");
                var caps = Fields(obj["capabilities"], "gaze", "pupil_diameter", "eyelid_openness", "confidence_informative");
                var capabilities = new EyeCapabilities(Availability(caps["gaze"]), Availability(caps["pupil_diameter"]),
                    Availability(caps["eyelid_openness"]), Boolean(caps["confidence_informative"]));
                var info = new SessionInfo(Text(obj["session_id"]), Text(obj["data_source"]), NullableText(obj["replay_origin"]),
                    Integer(obj["start_timestamp_us"]), Integer(obj["end_timestamp_us"]), capabilities);
                var samples = new List<EyeSample>();
                foreach (var token in Array(obj["samples"]))
                {
                    var s = Fields(token, "timestamp_us", "left", "right", "head_pose");
                    samples.Add(new EyeSample(Integer(s["timestamp_us"]), Eye(s["left"]), Eye(s["right"]), Head(s["head_pose"])));
                }
                var events = new List<ProbeEvent>();
                foreach (var token in Array(obj["probe_events"]))
                {
                    var p = Fields(token, "timestamp_us", "probe_id", "kind", "target_direction");
                    events.Add(new ProbeEvent(Integer(p["timestamp_us"]), Text(p["probe_id"]), Text(p["kind"]), Vector(p["target_direction"])));
                }
                return new EyeSession(info, samples, events);
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is OverflowException)
            { throw new FormatException("Invalid eye-session.v1 document: " + error.Message, error); }
        }

        public static string SerializeQuality(QualityResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new JObject
            {
                ["schema_version"] = "signal-quality.v1",
                ["session_id"] = result.Info.SessionId,
                ["data_source"] = result.Info.DataSource,
                ["replay_origin"] = Nullable(result.Info.ReplayOrigin),
                ["duration_ms"] = result.Info.DurationUs / 1000.0,
                ["policy"] = new JObject
                {
                    ["version"] = result.Policy.Version,
                    ["maximum_gap_us"] = result.Policy.MaximumGapUs,
                    ["minimum_valid_duration_us"] = result.Policy.MinimumValidDurationUs,
                    ["minimum_coverage"] = result.Policy.MinimumCoverage,
                    ["minimum_valid_samples"] = result.Policy.MinimumValidSamples
                },
                ["capabilities"] = new JObject
                {
                    ["gaze"] = AvailabilityJson(result.Info.Capabilities.Gaze),
                    ["pupil_diameter"] = AvailabilityJson(result.Info.Capabilities.PupilDiameter),
                    ["eyelid_openness"] = AvailabilityJson(result.Info.Capabilities.EyelidOpenness),
                    ["confidence_informative"] = result.Info.Capabilities.ConfidenceInformative
                },
                ["quality"] = new JObject
                {
                    ["status"] = result.Status,
                    ["valid_coverage"] = result.ValidCoverage,
                    ["valid_duration_ms"] = result.ValidDurationUs / 1000.0,
                    ["gap_duration_ms"] = result.GapDurationUs / 1000.0,
                    ["longest_gap_ms"] = result.LongestGapUs / 1000.0,
                    ["reasons"] = new JArray(result.Reasons),
                    ["counts"] = new JObject
                    {
                        ["input"] = result.InputSamples,
                        ["accepted"] = result.AcceptedSamples,
                        ["valid_gaze"] = result.ValidGazeSamples,
                        ["invalid_gaze"] = result.InvalidGazeSamples,
                        ["duplicate_timestamps"] = result.DuplicateTimestamps,
                        ["out_of_order_timestamps"] = result.OutOfOrderTimestamps,
                        ["out_of_bounds_timestamps"] = result.OutOfBoundsTimestamps
                    }
                }
            }.ToString(Formatting.Indented);
        }

        private static JToken Nullable(string value) { return value == null ? JValue.CreateNull() : new JValue(value); }
        private static JObject AvailabilityJson(SignalAvailability value)
        { return new JObject { ["available"] = value.Available, ["reason"] = Nullable(value.Reason) }; }
        private static SignalAvailability Availability(JToken token)
        {
            var o = Fields(token, "available", "reason");
            return new SignalAvailability(Boolean(o["available"]), NullableText(o["reason"]));
        }
        private static EyeReading Eye(JToken token)
        {
            var o = Fields(token, "valid", "direction", "confidence");
            return new EyeReading(Boolean(o["valid"]), o["direction"].Type == JTokenType.Null ? (Direction3?)null : Vector(o["direction"]),
                o["confidence"].Type == JTokenType.Null ? (double?)null : Number(o["confidence"], 0, 1));
        }
        private static HeadPose Head(JToken token)
        {
            if (token.Type == JTokenType.Null) return null;
            var o = Fields(token, "position_m", "rotation_xyzw");
            var q = Array(o["rotation_xyzw"]);
            if (q.Count != 4) throw new FormatException("Expected four quaternion coordinates.");
            var values = q.Select(t => Number(t)).ToArray();
            if (Math.Abs(values.Sum(v => v * v) - 1) > 0.001) throw new FormatException("Head rotation must be a unit quaternion.");
            return new HeadPose(Vector(o["position_m"]), values[0], values[1], values[2], values[3]);
        }
        private static Direction3 Vector(JToken token)
        {
            var o = Fields(token, "x", "y", "z");
            return new Direction3(Number(o["x"]), Number(o["y"]), Number(o["z"]));
        }
        internal static JObject ReadObject(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 })
            {
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new FormatException("Trailing JSON content.");
                return root;
            }
        }
        internal static JObject Fields(JToken token, params string[] fields)
        {
            var o = token as JObject;
            if (o == null || o.Properties().Count() != fields.Length || fields.Any(f => o.Property(f) == null) ||
                o.Properties().Any(p => !fields.Contains(p.Name))) throw new FormatException("Missing or unknown JSON fields.");
            return o;
        }
        private static JArray Array(JToken token)
        { return token as JArray ?? throw new FormatException("Expected JSON array."); }
        internal static string Text(JToken token)
        { if (token.Type != JTokenType.String) throw new FormatException("Expected string."); return token.Value<string>(); }
        internal static string NullableText(JToken token) { return token.Type == JTokenType.Null ? null : Text(token); }
        private static bool Boolean(JToken token)
        { if (token.Type != JTokenType.Boolean) throw new FormatException("Expected boolean."); return token.Value<bool>(); }
        internal static long Integer(JToken token)
        {
            if (token.Type != JTokenType.Integer) throw new FormatException("Expected integer timestamp.");
            long value;
            if (!long.TryParse(token.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Timestamps must be nonnegative signed 64-bit integers.");
            return value;
        }
        private static double Number(JToken token, double minimum = double.MinValue, double maximum = double.MaxValue)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new FormatException("Expected finite number.");
            double value = token.Value<double>();
            if (!Direction3.Finite(value) || value < minimum || value > maximum) throw new FormatException("Number outside allowed range.");
            return value;
        }
    }
}
