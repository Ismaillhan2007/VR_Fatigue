using System.Numerics;

namespace App.Core.Capture
{
    /// <summary>
    /// One eye-tracking row. Directions are unit vectors in the head frame (x right, y up, z forward).
    /// Times are seconds. Never assume a sample rate: use <see cref="SensorTimestamp"/> deltas.
    /// </summary>
    public struct GazeSample
    {
        /// <summary>App frame time when the row was read (e.g. Time.realtimeSinceStartupAsDouble).</summary>
        public double FrameTime;
        /// <summary>Provider/sensor timestamp. May repeat across frames; metrics must drop repeats.</summary>
        public double SensorTimestamp;
        /// <summary>False during tracking loss. Invalid rows are kept so gaps stay visible.</summary>
        public bool Valid;
        public bool LeftValid;
        public bool RightValid;
        public Vector3 LeftDirection;
        public Vector3 RightDirection;
        /// <summary>Combined gaze direction (average of valid eyes).</summary>
        public Vector3 CyclopeanDirection;
        /// <summary>Head pose in tracking space (metres / rotation).</summary>
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        /// <summary>Null when the provider has no confidence. Constant on Quest Pro: never filter on it.</summary>
        public float? Confidence;
    }

    /// <summary>A probe target shown during a task (CONCEPT 5.1). Metrics compare gaze against it.</summary>
    public struct ProbeEvent
    {
        /// <summary>Target onset on the same clock as <see cref="GazeSample.FrameTime"/>.</summary>
        public double OnsetTime;
        public double HoldSeconds;
        /// <summary>Target direction in the head frame.</summary>
        public Vector3 TargetDirection;
    }
}
