using System;
using System.Collections.Generic;

namespace GhostFret.Core.Strum
{
    public enum StrumDirection { None, Down, Up }

    /// <summary>
    /// Works out down- vs up-strums from the strumming hand's movement.
    /// Feed it the wrist position projected onto the "strum axis":
    /// a unit vector pointing from the low E string toward the high E string
    /// (for a normally held guitar this points roughly toward the floor).
    /// Positive velocity along that axis = down-strum.
    /// </summary>
    public sealed class StrumDirectionClassifier
    {
        public double MinSpeed = 0.25;           // m/s; slower movement isn't a strum
        public double LookbackSeconds = 0.06;    // velocity measured over the last 60 ms
        public double KeepSeconds = 1.0;

        private readonly List<KeyValuePair<double, double>> _samples = new List<KeyValuePair<double, double>>();

        public void AddSample(double timeSeconds, double positionAlongAxis)
        {
            _samples.Add(new KeyValuePair<double, double>(timeSeconds, positionAlongAxis));
            while (_samples.Count > 0 && timeSeconds - _samples[0].Key > KeepSeconds) _samples.RemoveAt(0);
        }

        /// <summary>Direction of hand movement around a strum time (from audio onset).</summary>
        public StrumDirection DirectionAt(double timeSeconds)
        {
            double velocity;
            if (!TryVelocity(timeSeconds, out velocity)) return StrumDirection.None;
            if (Math.Abs(velocity) < MinSpeed) return StrumDirection.None;
            return velocity > 0 ? StrumDirection.Down : StrumDirection.Up;
        }

        public bool TryVelocity(double timeSeconds, out double velocity)
        {
            velocity = 0;
            int end = -1;
            for (int i = _samples.Count - 1; i >= 0; i--)
            {
                if (_samples[i].Key <= timeSeconds + 1e-9) { end = i; break; }
            }
            if (end < 1) return false;

            int start = end;
            while (start > 0 && _samples[end].Key - _samples[start - 1].Key <= LookbackSeconds + 1e-9) start--;
            if (start == end) start = end - 1;

            double dt = _samples[end].Key - _samples[start].Key;
            if (dt <= 0) return false;
            velocity = (_samples[end].Value - _samples[start].Value) / dt;
            return true;
        }
    }
}
