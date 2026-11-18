using System;

namespace GhostFret.Core.Lesson
{
    /// <summary>
    /// Slows songs down when they're hard, speeds them up as the player improves.
    /// Factor 1.0 = original tempo.
    /// </summary>
    public sealed class AdaptiveTempo
    {
        public double MinFactor = 0.5;
        public double MaxFactor = 1.0;
        public double SlowDownBelow = 0.6;   // clean-chord rate
        public double SpeedUpAbove = 0.9;
        public int GoodRunsToSpeedUp = 2;
        public double SlowDownStep = 0.10;
        public double SpeedUpStep = 0.05;

        public double Factor { get; private set; }
        private int _goodRuns;

        public AdaptiveTempo(double startFactor = 0.75)
        {
            Factor = startFactor;
        }

        public double CurrentBpm(double originalBpm)
        {
            return originalBpm * Factor;
        }

        /// <summary>Call after each full run of a song or section.</summary>
        public void RecordRun(double cleanRate)
        {
            if (cleanRate < SlowDownBelow)
            {
                Factor = Math.Max(MinFactor, Factor - SlowDownStep);
                _goodRuns = 0;
            }
            else if (cleanRate > SpeedUpAbove)
            {
                _goodRuns++;
                if (_goodRuns >= GoodRunsToSpeedUp)
                {
                    Factor = Math.Min(MaxFactor, Factor + SpeedUpStep);
                    _goodRuns = 0;
                }
            }
            else
            {
                _goodRuns = 0;
            }
            Factor = Math.Round(Factor, 4);
        }
    }
}
