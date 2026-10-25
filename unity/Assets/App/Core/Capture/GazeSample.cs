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
        /// <summary>False when the source has no head pose for this row; head metrics then stay unavailable.</summary>
        public bool HeadTracked;
        /// <summary>Head pose in tracking space (metres / rotation). Meaningful only when <see cref="HeadTracked"/>.</summary>
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        /// <summary>Null when the provider has no confidence. Constant on Quest Pro: never filter on it.</summary>
        public float? Confidence;
    }

    /// <summary>
    /// One probe trial (CONCEPT 5.1), all times on the app frame clock (the same clock as <see cref="GazeSample.FrameTime"/>).
    /// The metrics bridge maps them onto the sensor clock. Onset -> HoldStart is the acquisition window.
    /// </summary>
    public struct ProbeEvent
    {
        /// <summary>Unique within a session, e.g. "b012" (baseline) or "l004" (loaded task).</summary>
        public string TrialId;
        public double OnsetTime;
        public double HoldStartTime;
        public double EndTime;
        /// <summary>Target direction in the head frame (head-locked target).</summary>
        public Vector3 TargetDirection;
    }
}
