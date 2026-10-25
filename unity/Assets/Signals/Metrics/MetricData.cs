using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VRFatigue.Signals
{
    public sealed class MetricPolicy
    {
        public const string AlgorithmVersion = "gaze-metrics.v1";
        public string Version { get; private set; }
        public string GazeEye { get; private set; }
        public int SmoothingWindow { get; private set; }
        public double ProbeToleranceDeg { get; private set; }
        public long AcquisitionDeadlineUs { get; private set; }
        public double MinimumHoldCoverage { get; private set; }
        public long MinimumHoldDurationUs { get; private set; }
        public double MinimumTargetSeparationDeg { get; private set; }
        public MetricPolicy(string version = "metric-policy.v1", string gazeEye = "auto", int smoothingWindow = 7,
            double probeToleranceDeg = 3, long acquisitionDeadlineUs = 1000000,
            double minimumHoldCoverage = 0.8, long minimumHoldDurationUs = 100000, double minimumTargetSeparationDeg = 6)
        {
            if (string.IsNullOrWhiteSpace(version) || (gazeEye != "auto" && gazeEye != "left" && gazeEye != "right") ||
                smoothingWindow < 3 || smoothingWindow > 101 || smoothingWindow % 2 == 0 ||
                !Direction3.Finite(probeToleranceDeg) || probeToleranceDeg <= 0 || probeToleranceDeg >= 180 ||
                acquisitionDeadlineUs <= 0 || minimumHoldDurationUs <= 0 ||
                !Direction3.Finite(minimumHoldCoverage) || minimumHoldCoverage <= 0 || minimumHoldCoverage > 1 ||
                !Direction3.Finite(minimumTargetSeparationDeg) || minimumTargetSeparationDeg < 0 || minimumTargetSeparationDeg > 180)
                throw new ArgumentException("Invalid metric policy.");
            Version = version; GazeEye = gazeEye; SmoothingWindow = smoothingWindow;
            ProbeToleranceDeg = probeToleranceDeg; AcquisitionDeadlineUs = acquisitionDeadlineUs;
            MinimumHoldCoverage = minimumHoldCoverage; MinimumHoldDurationUs = minimumHoldDurationUs;
            MinimumTargetSeparationDeg = minimumTargetSeparationDeg;
        }
        public MetricPolicy WithGazeEye(string eye)
        {
            return new MetricPolicy(Version, eye, SmoothingWindow, ProbeToleranceDeg, AcquisitionDeadlineUs,
                MinimumHoldCoverage, MinimumHoldDurationUs, MinimumTargetSeparationDeg);
        }
    }

    public sealed class MetricReading
    {
        public double? Value { get; private set; }
        public string UnavailableReason { get; private set; }
        private MetricReading(double? value, string reason) { Value = value; UnavailableReason = reason; }
        internal static MetricReading Measured(double value)
        {
            if (!Direction3.Finite(value) || value < 0) throw new ArgumentException("Metric must be finite and nonnegative.");
            return new MetricReading(value, null);
        }
        internal static MetricReading Unavailable(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Unavailable metric needs a reason.");
            return new MetricReading(null, reason);
        }
    }

    public sealed class ProbeMeasurement
    {
        public string ProbeId { get; internal set; }
        public string Outcome { get; internal set; }
        public double? LatencyMs { get; internal set; }
        public string AcquisitionUnavailableReason { get; internal set; }
        public double? HoldRmsErrorDeg { get; internal set; }
        public string HoldUnavailableReason { get; internal set; }
        public double? AcquisitionWindowMs { get; internal set; }
        public double? AcquisitionValidDurationMs { get; internal set; }
        public double? HoldDurationMs { get; internal set; }
        public double? HoldValidDurationMs { get; internal set; }
        internal double HoldSquaredErrorIntegral;
        internal long HoldValidDurationUs;
    }

    public sealed class MetricsResult
    {
        public QualityResult Quality { get; internal set; }
        public MetricPolicy Policy { get; internal set; }
        public string SelectedEye { get; internal set; }
        public ReadOnlyDictionary<string, MetricReading> Metrics { get; internal set; }
        public ReadOnlyCollection<ProbeMeasurement> Probes { get; internal set; }
        public long GazeValidDurationUs { get; internal set; }
        public long GazeSpeedDurationUs { get; internal set; }
        public int GazeSpeedIntervalCount { get; internal set; }
        public long HeadSpeedDurationUs { get; internal set; }
        public int HeadSpeedIntervalCount { get; internal set; }
    }

    internal sealed class GazePoint
    {
        internal readonly long Time;
        internal readonly Direction3 Direction;
        internal GazePoint(long time, Direction3 direction) { Time = time; Direction = direction; }
    }

    internal struct WeightedValue
    {
        internal readonly double Value;
        internal readonly long DurationUs;
        internal WeightedValue(double value, long durationUs) { Value = value; DurationUs = durationUs; }
    }
}
