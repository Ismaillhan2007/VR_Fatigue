using System;
using System.IO;
using App.Core.Analysis;
using App.Core.Contracts;
using UnityEngine;

namespace App.Tests
{
    /// <summary>Reads the shared synthetic fixtures from the repository root (no copies).</summary>
    static class TestFixtures
    {
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "fixtures"));

        public static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

        public static T Load<T>(string relative) where T : class, IContract => ContractJson.Parse<T>(Read(relative));

        /// <summary>Maps a fixture file name to its contract type by prefix.</summary>
        public static Type TypeFor(string fileName)
        {
            if (fileName.StartsWith("session_")) return typeof(SessionSummary);
            if (fileName.StartsWith("self_report_")) return typeof(SelfReport);
            if (fileName.StartsWith("preferences_")) return typeof(Preferences);
            if (fileName.StartsWith("scene_config_")) return typeof(SceneConfig);
            if (fileName.StartsWith("analysis_")) return typeof(AnalysisResponse);
            if (fileName.StartsWith("analyze_request_")) return typeof(AnalyzeRequest);
            return null;
        }

        public static IContract Parse(Type t, string json) =>
            (IContract)typeof(ContractJson).GetMethod(nameof(ContractJson.Parse)).MakeGenericMethod(t).Invoke(null, new object[] { json });
    }
}
