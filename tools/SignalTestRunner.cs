using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VRFatigue.Signals;

// Executes the same parameterless NUnit cases outside Unity; no scene or license required.
internal static class SignalTestRunner
{
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            try
            {
                var session = SignalJson.ParseSession(File.ReadAllText(args[0]));
                var replay = new ReplayEyeProvider(session);
                var batch = replay.ReadAvailable(replay.Info.DurationUs);
                if (args.Length > 2 && args[1] == "--baseline-plan")
                {
                    var plan = BaselineJson.ParsePlan(File.ReadAllText(args[2]));
                    var recorded = new EyeSession(replay.Info, batch.Samples, batch.ProbeEvents);
                    var comparison = BaselineEvaluator.Evaluate(recorded, plan);
                    Console.WriteLine(args.Length > 3 && args[3] == "--indicator" ?
                        IndicatorJson.Serialize(IndicatorEvaluator.Evaluate(comparison)) : BaselineJson.Serialize(comparison));
                    return 0;
                }
                var quality = QualityEvaluator.Evaluate(replay.Info, batch.Samples);
                Console.WriteLine(args.Length > 1 && args[1] == "--metrics" ?
                    MetricsJson.Serialize(MetricsEvaluator.Evaluate(quality, batch.ProbeEvents)) : SignalJson.SerializeQuality(quality));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
        int passed = 0, failed = 0;
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes().OrderBy(t => t.Name))
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes(typeof(TestAttribute), true).Length > 0).OrderBy(m => m.Name))
        {
            try { method.Invoke(Activator.CreateInstance(type), new object[0]); Console.WriteLine("PASS " + method.Name); passed++; }
            catch (Exception error)
            {
                var failure = error is TargetInvocationException ? error.InnerException : error;
                Console.Error.WriteLine("FAIL " + method.Name + "\n" + failure); failed++;
            }
        }
        Console.WriteLine(passed + " passed; " + failed + " failed");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}
