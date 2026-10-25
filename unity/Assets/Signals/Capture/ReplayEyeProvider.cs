using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VRFatigue.Signals
{
    public interface IEyeTrackingProvider
    {
        SessionInfo Info { get; }
        bool IsComplete { get; }
        EyeTrackingBatch ReadAvailable(long elapsedUs);
        void Reset();
    }

    public sealed class EyeTrackingBatch
    {
        public ReadOnlyCollection<EyeSample> Samples { get; private set; }
        public ReadOnlyCollection<ProbeEvent> ProbeEvents { get; private set; }
        internal EyeTrackingBatch(List<EyeSample> samples, List<ProbeEvent> events)
        { Samples = samples.AsReadOnly(); ProbeEvents = events.AsReadOnly(); }
    }

    // Caller supplies elapsed monotonic session time. No wall clock, sleeping or assumed sensor rate.
    public sealed class ReplayEyeProvider : IEyeTrackingProvider
    {
        private readonly EyeSession session;
        private readonly long[] dueTimes;
        private int sampleIndex, eventIndex;
        private long previousElapsed = -1;
        public SessionInfo Info { get; private set; }
        public bool IsComplete { get { return previousElapsed >= Info.DurationUs && sampleIndex == session.Samples.Count && eventIndex == session.ProbeEvents.Count; } }

        public ReplayEyeProvider(EyeSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            Info = session.Info.AsReplay();
            dueTimes = new long[session.Samples.Count];
            long lastDue = 0;
            for (int i = 0; i < dueTimes.Length; i++)
            {
                // Preserve arrival order, including bad timestamps, for the quality evaluator.
                long stamp = session.Samples[i].TimestampUs;
                long due = stamp <= Info.StartTimestampUs ? 0 : stamp >= Info.EndTimestampUs ? Info.DurationUs : stamp - Info.StartTimestampUs;
                lastDue = Math.Max(lastDue, due);
                dueTimes[i] = lastDue;
            }
        }

        public EyeTrackingBatch ReadAvailable(long elapsedUs)
        {
            if (elapsedUs < 0 || elapsedUs < previousElapsed) throw new ArgumentOutOfRangeException(nameof(elapsedUs), "Replay time must not move backwards; use Reset.");
            previousElapsed = elapsedUs;
            var samples = new List<EyeSample>();
            var events = new List<ProbeEvent>();
            while (sampleIndex < dueTimes.Length && dueTimes[sampleIndex] <= elapsedUs) samples.Add(session.Samples[sampleIndex++]);
            while (eventIndex < session.ProbeEvents.Count && session.ProbeEvents[eventIndex].TimestampUs - Info.StartTimestampUs <= elapsedUs)
                events.Add(session.ProbeEvents[eventIndex++]);
            return new EyeTrackingBatch(samples, events);
        }

        public void Reset() { sampleIndex = 0; eventIndex = 0; previousElapsed = -1; }
    }
}
