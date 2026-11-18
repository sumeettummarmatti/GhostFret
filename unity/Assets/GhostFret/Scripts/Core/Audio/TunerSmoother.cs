using System;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    public enum TunerAdvice
    {
        Listening,   // nothing stable yet: show "Listening…"
        TuneUp,      // flat
        TuneDown,    // sharp
        InTune
    }

    /// <summary>
    /// Turns raw, jumpy pitch readings (~10 per second) into a calm tuner display (DESIGN.md §7.1, §11.2).
    ///  1. Noise gate: readings with low pitch confidence are ignored.
    ///  2. Octave fix: pitch trackers sometimes report half or double the true pitch. We also try
    ///     f/2 and 2f and keep whichever lands nearest a string (unshifted gets a head start).
    ///  3. String lock: switching to another string needs a few readings in a row, so one stray
    ///     reading never flips the display.
    ///  4. Median of the last few readings (drops outliers), then a gentle moving average (calm needle).
    ///  5. "Stable" only after several readings agree; until then the panel says "Listening…".
    ///  6. A string is done when it stays within ±InTuneCents for InTuneHoldSeconds.
    /// Pure logic: feed it readings with a time, read the state back.
    /// </summary>
    public sealed class TunerSmoother
    {
        /// <summary>YIN confidence (1 − aperiodicity) needed to use a reading.</summary>
        public double MinConfidence = 0.8;
        /// <summary>Median over this many recent readings.</summary>
        public int MedianWindow = 5;
        /// <summary>Moving-average weight of each new median (0..1). Higher = quicker needle.</summary>
        public double SmoothingAlpha = 0.5;
        /// <summary>Readings in a row that must agree before the display is "stable".</summary>
        public int StableReadings = 3;
        /// <summary>A reading agrees if it's within this many cents of the median.</summary>
        public double AgreeCents = 25;
        /// <summary>Readings in a row on another string before we switch to it.</summary>
        public int SwitchReadings = 3;
        /// <summary>Within ± this many cents counts as in tune (DESIGN: ±5 cents).</summary>
        public double InTuneCents = 5;
        /// <summary>How long a string must stay in tune to be ticked off.</summary>
        public double InTuneHoldSeconds = 0.5;
        /// <summary>After this long with no usable reading we go back to "Listening…".</summary>
        public double LostAfterSeconds = 0.6;
        /// <summary>Head start (cents) for the unshifted reading over an octave-folded one.</summary>
        public double OctaveFoldPenaltyCents = 40;
        /// <summary>Readings further than this from any string (after folding) are ignored.</summary>
        public double MaxCentsFromString = 150;

        public GuitarTuning Tuning { get; private set; }

        public bool IsStable { get; private set; }
        /// <summary>String being shown (0 = low E … 5 = high E), or −1 if none yet.</summary>
        public int StringIndex { get; private set; }
        /// <summary>Smoothed offset from the string's target pitch. + = sharp, − = flat.</summary>
        public double Cents { get; private set; }
        /// <summary>Smoothed frequency in Hz (after octave folding).</summary>
        public double Frequency { get; private set; }
        /// <summary>String ticked off by the latest Push, or −1.</summary>
        public int JustFinishedString { get; private set; }

        private readonly bool[] _done = new bool[6];
        private readonly double[] _history = new double[16];
        private readonly double[] _sortScratch = new double[16];
        private int _historyCount, _historyNext;
        private int _agreeRun, _disagreeRun;
        private int _candidate = -1, _candidateRun;
        private double _lastValidTime = double.NegativeInfinity;
        private double _inTuneSince = double.NaN;

        public TunerSmoother(GuitarTuning tuning)
        {
            Tuning = tuning;
            Reset();
        }

        public void SetTuning(GuitarTuning tuning)
        {
            Tuning = tuning;
            Reset();
        }

        public bool IsDone(int stringIndex) { return _done[stringIndex]; }

        public bool AllDone
        {
            get { for (int s = 0; s < 6; s++) if (!_done[s]) return false; return true; }
        }

        public int DoneCount
        {
            get { int n = 0; for (int s = 0; s < 6; s++) if (_done[s]) n++; return n; }
        }

        public TunerAdvice Advice
        {
            get
            {
                if (!IsStable) return TunerAdvice.Listening;
                if (Math.Abs(Cents) <= InTuneCents) return TunerAdvice.InTune;
                return Cents < 0 ? TunerAdvice.TuneUp : TunerAdvice.TuneDown;
            }
        }

        public static string AdviceText(TunerAdvice advice)
        {
            switch (advice)
            {
                case TunerAdvice.TuneUp: return "Tune up a little";
                case TunerAdvice.TuneDown: return "Tune down a little";
                case TunerAdvice.InTune: return "Perfect";
                default: return "Listening…";
            }
        }

        /// <summary>Forget everything, including which strings are done.</summary>
        public void Reset()
        {
            Array.Clear(_done, 0, 6);
            ResetReading();
        }

        private void ResetReading()
        {
            IsStable = false;
            StringIndex = -1;
            Cents = 0;
            Frequency = 0;
            JustFinishedString = -1;
            _historyCount = 0;
            _historyNext = 0;
            _agreeRun = 0;
            _disagreeRun = 0;
            _candidate = -1;
            _candidateRun = 0;
            _inTuneSince = double.NaN;
        }

        public void Push(PitchResult pitch, double timeSeconds)
        {
            Push(pitch.HasPitch ? pitch.Frequency : 0, pitch.HasPitch ? pitch.Confidence : 0, timeSeconds);
        }

        /// <summary>Feed one reading. frequency ≤ 0 means "no pitch".</summary>
        public void Push(double frequency, double confidence, double timeSeconds)
        {
            JustFinishedString = -1;

            int s = -1;
            double cents = 0;
            bool usable = frequency > 0 && confidence >= MinConfidence && Resolve(frequency, out s, out cents);
            if (!usable)
            {
                // One dropped reading is normal (pluck noise); only a longer gap resets the display.
                if (timeSeconds - _lastValidTime > LostAfterSeconds && StringIndex >= 0) ResetReading();
                return;
            }
            _lastValidTime = timeSeconds;

            // String lock with hysteresis.
            if (StringIndex < 0)
            {
                StringIndex = s;
            }
            else if (s != StringIndex)
            {
                if (s == _candidate) _candidateRun++;
                else { _candidate = s; _candidateRun = 1; }
                if (_candidateRun < SwitchReadings) return;   // ignore until it's clearly a new string
                int keep = s;
                ResetReading();
                StringIndex = keep;
                _lastValidTime = timeSeconds;
            }
            _candidate = -1;
            _candidateRun = 0;

            AddHistory(cents);
            double median = Median();
            if (Math.Abs(cents - median) <= AgreeCents) { _agreeRun++; _disagreeRun = 0; }
            else { _agreeRun = 0; _disagreeRun++; }

            if (_historyCount == 1) Cents = median;
            else Cents += SmoothingAlpha * (median - Cents);
            Frequency = Tuning.OpenStringFrequency(StringIndex) * Math.Pow(2.0, Cents / 1200.0);

            if (_agreeRun >= StableReadings) IsStable = true;
            else if (_disagreeRun >= 2) IsStable = false;   // one odd reading doesn't blank the dial

            // In-tune hold timer.
            if (IsStable && Math.Abs(Cents) <= InTuneCents)
            {
                if (double.IsNaN(_inTuneSince)) _inTuneSince = timeSeconds;
                if (!_done[StringIndex] && timeSeconds - _inTuneSince >= InTuneHoldSeconds)
                {
                    _done[StringIndex] = true;
                    JustFinishedString = StringIndex;
                }
            }
            else _inTuneSince = double.NaN;
        }

        /// <summary>
        /// Picks string and cents for a raw frequency, also trying it an octave down and up.
        /// While a string is locked, a fold that lands on that string wins (pitch trackers'
        /// octave slips shouldn't move the needle).
        /// </summary>
        private bool Resolve(double frequency, out int stringIndex, out double cents)
        {
            stringIndex = -1;
            cents = 0;
            double bestCost = double.MaxValue;
            for (int fold = -1; fold <= 1; fold++)
            {
                double f = frequency * Math.Pow(2.0, fold);
                double c;
                int s = Tuning.NearestString(f, out c);
                double cost = Math.Abs(c) + (fold == 0 ? 0 : OctaveFoldPenaltyCents);
                if (StringIndex >= 0 && s == StringIndex && Math.Abs(c) <= MaxCentsFromString) cost -= 2 * OctaveFoldPenaltyCents;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    stringIndex = s;
                    cents = c;
                }
            }
            return stringIndex >= 0 && Math.Abs(cents) <= MaxCentsFromString;
        }

        private void AddHistory(double cents)
        {
            int window = Math.Max(1, Math.Min(MedianWindow, _history.Length));
            _history[_historyNext] = cents;
            _historyNext = (_historyNext + 1) % window;
            if (_historyCount < window) _historyCount++;
        }

        private double Median()
        {
            int n = _historyCount;
            Array.Copy(_history, _sortScratch, n);
            Array.Sort(_sortScratch, 0, n);
            return n % 2 == 1 ? _sortScratch[n / 2] : 0.5 * (_sortScratch[n / 2 - 1] + _sortScratch[n / 2]);
        }
    }
}
