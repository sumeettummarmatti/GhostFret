using System;
using GhostFret.Core.Feedback;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// The strum pipeline from DESIGN.md §8.1 / §11.3–11.4, as pure logic so it can be tested:
    ///   new mic samples → 1024-sample hops → spectral-flux onsets → exact onset time
    ///   → wait until 8192 samples after the attack exist → ChordAnalyzer → feedback.
    /// Push it whatever new samples arrived this frame (any count); it keeps its own ring copy.
    /// All buffers are allocated once, so a running listener doesn't allocate per frame.
    /// One strum = at most one chord result (results closer than MinSecondsBetweenChordResults merge).
    /// Not thread-safe: call from one thread.
    /// </summary>
    public sealed class StrumListener
    {
        public const int HopSize = 1024;            // ~21 ms at 48 kHz
        private const int RingSize = 1 << 15;       // ~0.68 s at 48 kHz: holds the analysis window plus slack
        private const int SubBlock = 64;            // onset refinement resolution (~1.3 ms)
        private const double FluxMinHz = 60, FluxMaxHz = 6000;   // flux band: strings and their brightest harmonics

        /// <summary>Skip this much after the attack before analysing: the pick noise isn't the chord.</summary>
        public double AttackSkipSeconds = 0.02;
        /// <summary>Onset noise gate: a hop quieter than this RMS can't start a strum (full scale = 1).</summary>
        public double MinHopRms = 0.004;
        /// <summary>A strum adds energy: a hop must be at least this much louder (RMS ratio) than the one before.
        /// Stops a ringing chord's beating or decay from counting as a new strum.</summary>
        public double MinLevelRise = 1.05;
        /// <summary>Strums within this window merge into one chord result ("one strum = one result").</summary>
        public double MinSecondsBetweenChordResults = 0.35;
        /// <summary>Log compression of magnitudes before flux, so loud and soft players both work.</summary>
        public double FluxCompression = 100;

        /// <summary>Onset detector, exposed for tuning (ThresholdMultiplier, MinFlux, MinIntervalSeconds).</summary>
        public readonly OnsetDetector Onsets = new OnsetDetector();
        public readonly ChordAnalyzer Analyzer;

        /// <summary>Chord to check strums against. Null or unknown = only report strum times.</summary>
        public string TargetChord;

        /// <summary>A strum started at this time (same clock as the times passed to Push).</summary>
        public event Action<double> StrumOnset;
        /// <summary>Feedback for a strum, with the strum's onset time.</summary>
        public event Action<ChordFeedback, double> ChordHeard;

        public int SampleRate { get; private set; }
        public long SamplesPushed { get { return _total; } }

        private readonly float[] _ring = new float[RingSize];
        private long _total;                 // samples ever pushed
        private long _nextHop;               // first sample of the next hop to analyse
        private double _newestTime;          // clock time of sample _total − 1

        private readonly float[] _hop = new float[HopSize];
        private readonly double[] _hopWindow = Fft.HannWindow(HopSize);
        private readonly double[] _re = new double[HopSize], _im = new double[HopSize];
        private readonly double[] _mags = new double[HopSize / 2];
        private readonly float[] _refine = new float[3 * HopSize];
        private readonly double[] _blockLevel = new double[3 * HopSize / SubBlock];
        private readonly float[] _analysis;
        private readonly int _fluxLo, _fluxHi;

        private long _pendingStart = -1;     // first sample of a scheduled chord analysis
        private double _pendingOnsetTime;
        private double _lastResultOnset = double.NegativeInfinity;
        private double _prevHopRms;

        public StrumListener(int sampleRate, GuitarTuning tuning)
        {
            SampleRate = sampleRate;
            Analyzer = new ChordAnalyzer(tuning);
            _analysis = new float[Analyzer.FftSize];
            Onsets.MinIntervalSeconds = 0.1;   // fast enough for eighth-note strumming at 140 BPM
            Onsets.MinFlux = 6.0;              // log-compressed units: a ringing chord's tail stays below ~5, strums give 8–40
            double binHz = (double)sampleRate / HopSize;
            _fluxLo = Math.Max(1, (int)(FluxMinHz / binHz));
            _fluxHi = Math.Min(HopSize / 2 - 1, (int)(FluxMaxHz / binHz));
        }

        /// <summary>Start fresh (e.g. after Listening was off). Doesn't clear TargetChord.</summary>
        public void Reset()
        {
            Array.Clear(_ring, 0, RingSize);
            _total = 0;
            _nextHop = 0;
            _pendingStart = -1;
            _lastResultOnset = double.NegativeInfinity;
            _prevHopRms = 0;
            Onsets.Reset();
        }

        /// <summary>
        /// Adds count new samples from src[offset…]. newestSampleTime = clock time of the last one
        /// (e.g. AudioSettings.dspTime mapped from the mic position).
        /// </summary>
        public void Push(float[] src, int offset, int count, double newestSampleTime)
        {
            if (count <= 0) return;
            // If more than the ring arrives at once (long hitch), keep only the newest part.
            if (count > RingSize)
            {
                offset += count - RingSize;
                _total += count - RingSize;
                count = RingSize;
                _nextHop = Math.Max(_nextHop, _total - HopSize);
                _pendingStart = -1;
            }
            int write = (int)(_total % RingSize);
            int first = Math.Min(count, RingSize - write);
            Array.Copy(src, offset, _ring, write, first);
            if (count > first) Array.Copy(src, offset + first, _ring, 0, count - first);
            _total += count;
            _newestTime = newestSampleTime;

            // Hops that fell out of the ring during a hitch can't be analysed.
            if (_total - _nextHop > RingSize - HopSize) _nextHop = _total - HopSize;

            while (_total - _nextHop >= HopSize)
            {
                ProcessHop(_nextHop);
                _nextHop += HopSize;
            }
            TryAnalyse();
        }

        /// <summary>Clock time of an absolute sample index.</summary>
        public double TimeOfSample(long index)
        {
            return _newestTime - (double)(_total - 1 - index) / SampleRate;
        }

        private void ProcessHop(long start)
        {
            CopyFromRing(start, _hop, 0, HopSize);
            double rms = Fft.Rms(_hop, 0, HopSize);

            Fft.MagnitudeSpectrum(_hop, 0, HopSize, _hopWindow, _re, _im, _mags);
            // Normalise so a full-scale sine ≈ 1, compress, and keep only the useful band.
            double norm = 4.0 / HopSize;
            for (int k = 0; k < _mags.Length; k++)
            {
                _mags[k] = (k < _fluxLo || k > _fluxHi) ? 0 : Math.Log(1 + FluxCompression * _mags[k] * norm);
            }

            double hopTime = TimeOfSample(start);
            bool onset = Onsets.Process(_mags, hopTime);
            bool louder = rms >= MinLevelRise * _prevHopRms;
            _prevHopRms = rms;
            if (!onset || !louder || rms < MinHopRms) return;

            long onsetSample = RefineOnset(start);
            double onsetTime = TimeOfSample(onsetSample);
            if (StrumOnset != null) StrumOnset(onsetTime);

            bool canAnalyse = ChordLibrary.Contains(TargetChord);
            bool merged = _pendingStart >= 0 || onsetTime - _lastResultOnset < MinSecondsBetweenChordResults;
            if (canAnalyse && !merged)
            {
                _pendingStart = onsetSample + (long)(AttackSkipSeconds * SampleRate);
                _pendingOnsetTime = onsetTime;
                _lastResultOnset = onsetTime;
            }
        }

        /// <summary>Where the level must climb to (0..1 between the quiet before and the peak) to mark the attack.</summary>
        public double OnsetRiseFraction = 0.1;
        /// <summary>A strum's strings all sound within this long, so the attack can't start earlier than this before the peak.</summary>
        public double MaxStrumSpreadSeconds = 0.03;

        /// <summary>
        /// Flux tells us which hop holds the attack (~21 ms). To time strums to a few ms, look at the
        /// level (RMS) of 64-sample blocks over the two hops before and this one, and find where it
        /// climbs OnsetRiseFraction of the way from the quiet before the attack to the loudest point.
        /// Works even when the previous chord is still ringing (its level is the "quiet").
        /// </summary>
        private long RefineOnset(long hopStart)
        {
            long from = Math.Max(0, Math.Max(hopStart - 2 * HopSize, _total - RingSize));
            int len = (int)Math.Min(3 * HopSize, _total - from);
            CopyFromRing(from, _refine, 0, len);
            int blocks = len / SubBlock;
            if (blocks < 2) return hopStart;

            int loudest = 0;
            double max = -1;
            for (int b = 0; b < blocks; b++)
            {
                double sum = 0;
                int start = b * SubBlock;
                for (int i = start; i < start + SubBlock; i++) sum += _refine[i] * _refine[i];
                _blockLevel[b] = Math.Sqrt(sum / SubBlock);
                if (_blockLevel[b] > max) { max = _blockLevel[b]; loudest = b; }
            }
            double min = double.MaxValue;
            for (int b = 0; b <= loudest; b++) min = Math.Min(min, _blockLevel[b]);
            double mark = min + OnsetRiseFraction * (max - min);
            // Search back from the peak for the last quiet block: the attack starts right after it.
            int onsetBlock = loudest;
            int earliest = Math.Max(0, loudest - (int)(MaxStrumSpreadSeconds * SampleRate / SubBlock));
            for (int b = loudest; b >= earliest; b--)
            {
                if (_blockLevel[b] < mark) break;
                onsetBlock = b;
            }
            return from + (long)onsetBlock * SubBlock;
        }

        private void TryAnalyse()
        {
            if (_pendingStart < 0) return;
            int n = _analysis.Length;
            if (_total < _pendingStart + n) return;            // not enough audio yet
            if (_total - _pendingStart > RingSize)              // fell out of the ring (long hitch)
            {
                _pendingStart = -1;
                return;
            }
            CopyFromRing(_pendingStart, _analysis, 0, n);
            _pendingStart = -1;
            if (!ChordLibrary.Contains(TargetChord)) return;   // target cleared while waiting
            ChordFeedback feedback = Analyzer.Analyze(_analysis, 0, n, SampleRate, TargetChord);
            if (ChordHeard != null) ChordHeard(feedback, _pendingOnsetTime);
        }

        private void CopyFromRing(long start, float[] dst, int dstOffset, int count)
        {
            int read = (int)(start % RingSize);
            int first = Math.Min(count, RingSize - read);
            Array.Copy(_ring, read, dst, dstOffset, first);
            if (count > first) Array.Copy(_ring, 0, dst, dstOffset + first, count - first);
        }
    }
}
