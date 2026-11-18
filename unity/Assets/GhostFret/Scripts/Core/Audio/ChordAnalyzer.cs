using GhostFret.Core.Feedback;
using GhostFret.Core.Music;

namespace GhostFret.Core.Audio
{
    /// <summary>
    /// One call to go from "audio just after a strum" to "feedback for the player".
    /// Window → FFT → chroma → chord match → per-string check → feedback.
    /// </summary>
    public sealed class ChordAnalyzer
    {
        public const int DefaultFftSize = 8192;   // ~170 ms at 48 kHz: good low-note resolution
        public double MinRms = 0.01;

        private readonly GuitarTuning _tuning;
        private readonly ChordMatcher _matcher;
        private readonly StringChecker _strings = new StringChecker();
        private readonly ChordFeedbackEvaluator _evaluator = new ChordFeedbackEvaluator();
        private readonly int _fftSize;
        private readonly double[] _window;

        public ChordAnalyzer(GuitarTuning tuning, int fftSize = DefaultFftSize)
        {
            _tuning = tuning;
            _matcher = new ChordMatcher(tuning);
            _fftSize = fftSize;
            _window = Fft.HannWindow(fftSize);
        }

        public ChordFeedback Analyze(float[] samplesAfterStrum, int sampleRate, string targetChord)
        {
            ChordShape target = ChordLibrary.Get(targetChord);
            if (Fft.Rms(samplesAfterStrum) < MinRms) return ChordFeedback.TooQuiet();

            double[] mags = Fft.MagnitudeSpectrum(samplesAfterStrum, _fftSize, _window);
            double[] chroma = ChromaExtractor.FromSpectrum(mags, sampleRate, _fftSize);
            ChordMatchResult match = _matcher.Match(chroma, targetChord);
            StringCheckResult strings = _strings.Check(mags, sampleRate, _fftSize, target, _tuning);
            return _evaluator.Evaluate(target, match, strings);
        }
    }
}
