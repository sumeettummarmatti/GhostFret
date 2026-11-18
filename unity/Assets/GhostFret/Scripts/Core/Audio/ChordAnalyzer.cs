using GhostFret.Core.Feedback;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// One call to go from "audio just after a strum" to "feedback for the player".
    /// Window → FFT → chroma → chord match → per-string check → feedback.
    /// Scratch buffers are reused, so analysing a strum doesn't churn memory on Quest.
    /// Not thread-safe: use one analyzer per thread.
    /// </summary>
    public sealed class ChordAnalyzer
    {
        public const int DefaultFftSize = 8192;   // ~170 ms at 48 kHz: good low-note resolution
        /// <summary>Noise gate: quieter than this (RMS, full scale = 1) is "I couldn't hear that".</summary>
        public double MinRms = 0.01;

        /// <summary>Exposed so their thresholds can be tuned on device.</summary>
        public readonly ChordMatcher Matcher;
        public readonly StringChecker Strings = new StringChecker();
        public readonly ChordFeedbackEvaluator Evaluator = new ChordFeedbackEvaluator();

        private readonly GuitarTuning _tuning;
        private readonly int _fftSize;
        private readonly double[] _window;
        private readonly double[] _re, _im, _mags;
        private readonly double[] _chroma = new double[12];

        public ChordAnalyzer(GuitarTuning tuning, int fftSize = DefaultFftSize)
        {
            _tuning = tuning;
            Matcher = new ChordMatcher(tuning);
            _fftSize = fftSize;
            _window = Fft.HannWindow(fftSize);
            _re = new double[fftSize];
            _im = new double[fftSize];
            _mags = new double[fftSize / 2];
        }

        public int FftSize { get { return _fftSize; } }

        public ChordFeedback Analyze(float[] samplesAfterStrum, int sampleRate, string targetChord)
        {
            return Analyze(samplesAfterStrum, 0, samplesAfterStrum.Length, sampleRate, targetChord);
        }

        /// <summary>Analyses count samples starting at offset (only the first FftSize are used).</summary>
        public ChordFeedback Analyze(float[] samples, int offset, int count, int sampleRate, string targetChord)
        {
            ChordShape target = ChordLibrary.Get(targetChord);
            if (count > _fftSize) count = _fftSize;
            if (Fft.Rms(samples, offset, count) < MinRms) return ChordFeedback.TooQuiet();

            Fft.MagnitudeSpectrum(samples, offset, count, _window, _re, _im, _mags);
            ChromaExtractor.FromSpectrum(_mags, sampleRate, _fftSize, _chroma);
            ChordMatchResult match = Matcher.Match(_chroma, targetChord);
            StringCheckResult strings = Strings.Check(_mags, sampleRate, _fftSize, target, _tuning);
            return Evaluator.Evaluate(target, match, strings);
        }
    }
}
