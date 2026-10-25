using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace App.Core.Contracts
{
    public sealed class ContractException : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public ContractException(string message, IReadOnlyList<string> problems)
            : base(message + ": " + string.Join("; ", problems)) => Problems = problems;
    }

    /// <summary>
    /// The only way contract JSON enters or leaves the app. Rejects unknown fields, duplicate keys, unknown or integer
    /// enum values, wrong primitive types, unknown schema_version and non-finite numbers; then runs IContract.Validate.
    /// </summary>
    public static class ContractJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            Culture = CultureInfo.InvariantCulture,
            MaxDepth = 32,
            Converters = { new StringEnumConverter { AllowIntegerValues = false }, new StrictPrimitiveConverter() },
        };

        static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
        };

        public static T Parse<T>(string json) where T : class, IContract
        {
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json ?? ""))
                       { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = 32 })
                {
                    var token = JToken.ReadFrom(reader, LoadSettings);
                    root = token as JObject ?? throw new JsonException("root must be a JSON object");
                    if (reader.Read()) throw new JsonException("trailing content after the root object");
                }
            }
            catch (JsonException e)
            {
                throw new ContractException($"Invalid {typeof(T).Name} JSON", new[] { e.Message });
            }

            var version = root["schema_version"];
            if (version == null || version.Type != JTokenType.String || (string)version != Problems.SchemaVersion)
                throw new ContractException($"Invalid {typeof(T).Name}",
                    new[] { $"$.schema_version: unsupported '{version}', expected '{Problems.SchemaVersion}'" });

            T value;
            try
            {
                value = root.ToObject<T>(Serializer);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                throw new ContractException($"Invalid {typeof(T).Name}", new[] { e.Message });
            }

            var problems = value.Validate();
            if (problems.Count > 0) throw new ContractException($"Invalid {typeof(T).Name}", problems);
            return value;
        }

        public static bool TryParse<T>(string json, out T value, out IReadOnlyList<string> problems) where T : class, IContract
        {
            try
            {
                value = Parse<T>(json);
                problems = Array.Empty<string>();
                return true;
            }
            catch (ContractException e)
            {
                value = null;
                problems = e.Problems;
                return false;
            }
        }

        /// <summary>Validates first so invalid or non-finite values never leave the app.</summary>
        public static string Serialize(IContract value, bool indented = false)
        {
            var problems = value.Validate();
            if (problems.Count > 0) throw new ContractException($"Refusing to serialise invalid {value.GetType().Name}", problems);
            return JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None, Settings);
        }

        /// <summary>
        /// JSON Schema types are strict: no string-to-number, number-to-string, string-to-bool or fractional-to-integer coercion.
        /// </summary>
        sealed class StrictPrimitiveConverter : JsonConverter
        {
            public override bool CanWrite => false;

            public override bool CanConvert(Type t) =>
                t == typeof(string) || t == typeof(bool) || t == typeof(bool?) || t == typeof(int) || t == typeof(int?)
                || t == typeof(long) || t == typeof(long?) || t == typeof(double) || t == typeof(double?);

            public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer)
            {
                var tok = reader.TokenType;
                var nullable = t == typeof(string) || Nullable.GetUnderlyingType(t) != null;
                if (tok == JsonToken.Null)
                {
                    if (nullable) return null;
                    throw Error(reader, "null is not allowed");
                }
                var core = Nullable.GetUnderlyingType(t) ?? t;
                if (core == typeof(string))
                    return tok == JsonToken.String ? (string)reader.Value : throw Error(reader, "expected a string");
                if (core == typeof(bool))
                    return tok == JsonToken.Boolean ? (bool)reader.Value : throw Error(reader, "expected a boolean");
                if (core == typeof(int) || core == typeof(long))
                {
                    if (tok != JsonToken.Integer) throw Error(reader, "expected an integer");
                    var v = Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
                    if (core == typeof(int)) return checked((int)v);
                    return v;
                }
                if (tok != JsonToken.Integer && tok != JsonToken.Float) throw Error(reader, "expected a number");
                var d = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) throw Error(reader, "non-finite numbers are not allowed");
                return d;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                throw new NotSupportedException();

            static JsonSerializationException Error(JsonReader reader, string message) =>
                new JsonSerializationException($"{reader.Path}: {message}");
        }
    }
}
