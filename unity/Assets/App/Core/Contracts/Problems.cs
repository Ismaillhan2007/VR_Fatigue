using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace App.Core.Contracts
{
    /// <summary>Implemented by every v1 DTO. Mirrors the JSON Schema rules that Newtonsoft cannot enforce.</summary>
    public interface IContract
    {
        void Validate(Problems problems, string path);
    }

    /// <summary>Collects validation errors as "path: message" strings.</summary>
    public sealed class Problems
    {
        public const string SchemaVersion = "1.0";

        static readonly Regex UuidV4 = new Regex(
            "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);

        readonly List<string> _items = new List<string>();

        public IReadOnlyList<string> Items => _items;
        public bool Any => _items.Count > 0;

        public void Add(string path, string message) => _items.Add(path + ": " + message);

        public void Version(string path, string value)
        {
            if (value != SchemaVersion) Add(path + ".schema_version", $"unsupported '{value}', expected '{SchemaVersion}'");
        }

        public void Const(string path, string value, string expected)
        {
            if (value != expected) Add(path, $"must be '{expected}'");
        }

        public void Range(string path, double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) Add(path, "must be a finite number");
            else if (value < min || value > max) Add(path, $"{value} outside [{min}, {max}]");
        }

        public void Range(string path, double? value, double min, double max)
        {
            if (value.HasValue) Range(path, value.Value, min, max);
        }

        public void Min(string path, long value, long min)
        {
            if (value < min) Add(path, $"{value} below {min}");
        }

        public void Required(string path, object value)
        {
            if (value == null) Add(path, "is required");
        }

        public void MaxLength(string path, string value, int max)
        {
            if (value != null && value.Length > max) Add(path, $"longer than {max} characters");
        }

        public void List<T>(string path, IList<T> list, int min, int max, bool unique)
        {
            if (list == null) { Add(path, "is required"); return; }
            if (list.Count < min || list.Count > max) Add(path, $"needs {min}..{max} items, has {list.Count}");
            if (unique && list.Distinct().Count() != list.Count) Add(path, "items must be unique");
        }

        public void Defined<TEnum>(string path, TEnum value) where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value)) Add(path, $"unknown value {value}");
        }

        public void Defined<TEnum>(string path, TEnum? value) where TEnum : struct, Enum
        {
            if (value.HasValue) Defined(path, value.Value);
        }

        public void Uuid(string path, string value)
        {
            if (value == null || !UuidV4.IsMatch(value)) Add(path, "must be a lowercase UUIDv4");
        }

        public void Nested(string path, IContract child)
        {
            if (child == null) Add(path, "is required");
            else child.Validate(this, path);
        }
    }

    public static class ContractValidation
    {
        public static IReadOnlyList<string> Validate(this IContract contract)
        {
            var problems = new Problems();
            problems.Nested("$", contract);
            return problems.Items;
        }

        public static bool IsValid(this IContract contract) => contract.Validate().Count == 0;
    }
}
