using System;
using System.Collections.Generic;
using System.Numerics;
using App.Core.Capture;
using App.Core.Contracts;

namespace App.Capture
{
    /// <summary>
    /// Seeded synthetic gaze (fixations + saccades) at a 90 Hz frame clock with headset-like artefacts: ~2% repeated
    /// sensor timestamps and occasional 2x frame gaps. Always labelled data_source = synthetic; never native eye gaze.
    /// </summary>
    public sealed class SyntheticEyeProvider : IEyeTrackingProvider, IProbeFollower
    {
        const double Dt = 1.0 / 90.0;
        readonly Random _rng;
        double _start, _nextTime, _sensorClock, _nextSaccade;
        Vector3 _target = Vector3.UnitZ;
        // Probe following: after ShowTarget the gaze jumps to the target (with a landing error) after a latency.
        bool _following, _loaded;
        Vector3 _pendingTarget;
        double _pendingAt = double.MaxValue;

        public SyntheticEyeProvider(int seed = 1) => _rng = new Random(seed);

        public string Name => "synthetic generator";
        public DataSource DataSource => DataSource.Synthetic;
        public ReplayOrigin? ReplayOrigin => null;
        public Capabilities Capabilities { get; } = new Capabilities
            { GazePose = true, NativeEyeGaze = false, Openness = false, Pupil = false, ConfidenceInformative = false };
        public IReadOnlyList<UnavailableSignal> Unavailable { get; } = EyeSignals.NotExposed(Signal.Openness, Signal.Pupil, Signal.Blink);
        public bool IsRunning { get; private set; }

        public void Start(double now)
        {
            _start = _nextTime = now;
            _sensorClock = 0;
            _nextSaccade = now + 0.4;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;

        public void SetLoaded(bool loaded) => _loaded = loaded;

        public void ShowTarget(Vector3 headLocalDirection, double now)
        {
            _following = true;
            var latency = Math.Max(0.14, Gauss(_loaded ? 0.228 : 0.205, _loaded ? 0.030 : 0.022));
            if (_rng.NextDouble() < (_loaded ? 0.07 : 0.02)) latency = 0.52 + _rng.NextDouble() * 0.2; // late saccade
            var error = Deg(Math.Abs(Gauss(0, _loaded ? 0.58 : 0.42)) * 1.25);
            var angle = _rng.NextDouble() * Math.PI * 2;
            _pendingTarget = Vector3.Normalize(headLocalDirection + new Vector3((float)(error * Math.Cos(angle)), (float)(error * Math.Sin(angle)), 0));
            _pendingAt = now + latency;
        }

        double Gauss(double mean, double sd) =>
            mean + sd * Math.Sqrt(-2 * Math.Log(1 - _rng.NextDouble())) * Math.Cos(2 * Math.PI * _rng.NextDouble());

        public int ReadSamples(List<GazeSample> buffer, double now)
        {
            if (!IsRunning) return 0;
            var added = 0;
            while (_nextTime <= now)
            {
                var t = _nextTime;
                var repeat = _rng.NextDouble() < 0.02;
                if (!repeat) _sensorClock += _rng.NextDouble() < 0.01 ? 2 * Dt : Dt;
                if (_following)
                {
                    if (t >= _pendingAt) { _target = _pendingTarget; _pendingAt = double.MaxValue; }
                }
                else if (t >= _nextSaccade)
                {
                    _target = Direction(Deg(_rng.NextDouble() * 24 - 12), Deg(_rng.NextDouble() * 16 - 8));
                    _nextSaccade = t + 0.25 + _rng.NextDouble() * 0.6;
                }
                var jitter = new Vector3((float)Noise(0.002), (float)Noise(0.002), 0);
                var dir = Vector3.Normalize(_target + jitter);
                buffer.Add(new GazeSample
                {
                    FrameTime = t,
                    SensorTimestamp = _sensorClock,
                    Valid = true,
                    LeftValid = true,
                    RightValid = true,
                    LeftDirection = dir,
                    RightDirection = dir,
                    CyclopeanDirection = dir,
                    HeadTracked = true,
                    HeadPosition = new Vector3(0, 1.6f, 0),
                    HeadRotation = Quaternion.Identity,
                    Confidence = null,
                });
                _nextTime += Dt;
                added++;
            }
            return added;
        }

        double Noise(double scale) => (_rng.NextDouble() * 2 - 1) * scale;
        static double Deg(double d) => d * Math.PI / 180;

        static Vector3 Direction(double yaw, double pitch) => Vector3.Normalize(new Vector3(
            (float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Cos(yaw) * Math.Cos(pitch))));
    }

    static class EyeSignals
    {
        public static IReadOnlyList<UnavailableSignal> NotExposed(params Signal[] signals)
        {
            var list = new List<UnavailableSignal>();
            foreach (var s in signals) list.Add(new UnavailableSignal { Signal = s, Reason = UnavailableReason.NotExposedByRuntime });
            return list;
        }
    }
}
