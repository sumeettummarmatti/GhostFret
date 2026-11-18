using System;
using System.Collections.Generic;
using System.Numerics;
using GhostFret.Core.Strum;

namespace GhostFret.Core.AirMode
{
    public struct AirStrum
    {
        /// <summary>When the hand crossed the middle of the strings (same clock as the samples).</summary>
        public double Time;
        public StrumDirection Direction;
        /// <summary>Hand speed across the strings at the crossing, m/s.</summary>
        public float Speed;
    }

    /// <summary>
    /// Turns the strumming hand's path into strums: a strum is the hand crossing the middle of the
    /// virtual strings fast enough, while near them. Down = moving toward the high E string.
    /// Hysteresis: after a strum the hand must move ArmDistance past the strings before the next one
    /// counts, so tracking jitter around the strings never double-strums.
    /// </summary>
    public sealed class AirStrumDetector
    {
        public float MinSpeed = 0.30f;              // m/s across the strings; slower is a drift, not a strum
        public float ArmDistance = 0.015f;          // hand must be this far past the strings to arm the next strum
        public double MinIntervalSeconds = 0.09;    // fastest strums we accept (16ths at ~160 bpm)
        public double LookbackSeconds = 0.05;       // speed measured over the last 50 ms (smooths jitter)

        public StrumZone Zone { get; set; }

        private readonly List<KeyValuePair<double, float>> _samples = new List<KeyValuePair<double, float>>();
        private int _armedSide;                      // −1 above the strings, +1 below, 0 not armed
        private bool _hasPrev;
        private double _prevTime;
        private float _prevAcross;
        private double _lastStrumTime = double.NegativeInfinity;

        public AirStrumDetector(StrumZone zone)
        {
            Zone = zone;
        }

        /// <summary>Forget the path (call when tracking is lost or the guitar moves).</summary>
        public void Reset()
        {
            _samples.Clear();
            _armedSide = 0;
            _hasPrev = false;
        }

        /// <summary>Feed one hand position. Returns true (with the strum) when a strum happened.</summary>
        public bool AddSample(double time, Vector3 position, out AirStrum strum)
        {
            strum = default(AirStrum);
            if (Zone == null) return false;
            if (_hasPrev && time <= _prevTime) return false; // duplicate or out-of-order frame

            float along, across, depth;
            Zone.Project(position, out along, out across, out depth);
            bool inReach = Zone.InReach(along, depth) && Math.Abs(across) <= Zone.HalfHeight;

            _samples.Add(new KeyValuePair<double, float>(time, across));
            while (_samples.Count > 2 && time - _samples[0].Key > LookbackSeconds * 3) _samples.RemoveAt(0);

            bool fired = false;
            if (_hasPrev && inReach && CrossedMiddle(_prevAcross, across))
            {
                int fromSide = _prevAcross < 0f || (_prevAcross == 0f && across > 0f) ? -1 : 1;
                float velocity = Velocity(time);
                bool movingRightWay = fromSide < 0 ? velocity > 0f : velocity < 0f;
                double crossTime = Interpolate(_prevTime, _prevAcross, time, across);

                if (_armedSide == fromSide && movingRightWay && Math.Abs(velocity) >= MinSpeed
                    && crossTime - _lastStrumTime >= MinIntervalSeconds)
                {
                    strum.Time = crossTime;
                    strum.Direction = velocity > 0f ? StrumDirection.Down : StrumDirection.Up;
                    strum.Speed = Math.Abs(velocity);
                    _lastStrumTime = crossTime;
                    fired = true;
                }
                // Any crossing (strum or slow drift) disarms until the hand is clearly past the strings.
                _armedSide = 0;
            }

            if (!inReach) _armedSide = 0;
            else if (across <= -ArmDistance) _armedSide = -1;
            else if (across >= ArmDistance) _armedSide = 1;

            _hasPrev = true;
            _prevTime = time;
            _prevAcross = across;
            return fired;
        }

        private static bool CrossedMiddle(float a, float b)
        {
            return (a < 0f && b >= 0f) || (a > 0f && b <= 0f);
        }

        private static double Interpolate(double t0, float a0, double t1, float a1)
        {
            float span = a1 - a0;
            if (Math.Abs(span) < 1e-9f) return t1;
            return t0 + (0f - a0) / span * (t1 - t0);
        }

        private float Velocity(double now)
        {
            int end = _samples.Count - 1;
            if (end < 1) return 0f;
            int start = end;
            while (start > 0 && now - _samples[start - 1].Key <= LookbackSeconds + 1e-9) start--;
            if (start == end) start = end - 1;
            double dt = _samples[end].Key - _samples[start].Key;
            if (dt <= 0) return 0f;
            return (float)((_samples[end].Value - _samples[start].Value) / dt);
        }
    }
}
