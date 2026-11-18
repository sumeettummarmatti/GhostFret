using GhostFret.Core.Audio;
using GhostFret.Core.Music;

namespace GhostFret.Core.Feedback
{
    public enum FeedbackKind
    {
        Clean,
        WeakString,       // a string that should ring is muted or very quiet
        UnwantedString,   // a string that should be skipped is ringing
        WrongChord,
        NotSure,          // not confident: give a gentle general tip, never a wrong claim
        TooQuiet
    }

    /// <summary>What the coach says after a strum, plus which string to highlight (−1 = none).</summary>
    public sealed class ChordFeedback
    {
        public FeedbackKind Kind;
        public string Message;
        public int StringIndex = -1;
        public string HeardChord;
        /// <summary>How well the sound matched the target chord's notes (cosine 0..1). For tuning and debugging only.</summary>
        public double TargetScore;

        public const string GeneralTip = "Almost! Check each finger against the ghost hand and strum again.";

        public static ChordFeedback TooQuiet()
        {
            return new ChordFeedback { Kind = FeedbackKind.TooQuiet, Message = "I couldn't hear that. Try a slightly firmer strum." };
        }

        public static ChordFeedback NotSure(string heardChord, double targetScore)
        {
            return new ChordFeedback { Kind = FeedbackKind.NotSure, Message = GeneralTip, HeardChord = heardChord, TargetScore = targetScore };
        }
    }

    /// <summary>
    /// Turns analysis into friendly, specific feedback.
    /// Tone rules: short, kind, one tip at a time, never shaming.
    /// Confidence rule (CLAUDE.md #5): every specific claim ("Clean", "the G string is muted",
    /// "that sounds like G") needs its own evidence bar. Anything below a bar becomes NotSure,
    /// which the lesson turns into a general tip. Being quiet is fine; being wrong is not.
    /// </summary>
    public sealed class ChordFeedbackEvaluator
    {
        /// <summary>Below this best-template score the sound isn't clearly any chord (noise, talking, one string).</summary>
        public double MinBestScore = 0.6;
        /// <summary>"That sounds like X" needs X to score at least this…</summary>
        public double MinWrongChordScore = 0.85;
        /// <summary>…and beat the target by at least this much…</summary>
        public double WrongChordMargin = 0.08;
        /// <summary>…and at least this share of the sound must be notes the target chord can't explain.</summary>
        public double MinUnexplainedForWrongChord = 0.08;
        /// <summary>"Clean" or a string tip needs the target chord to score at least this.</summary>
        public double MinTargetScore = 0.8;
        /// <summary>"Clean" needs no more than this share of peak energy that the chord's strings can't explain.</summary>
        public double MaxUnexplainedForClean = 0.15;
        /// <summary>String tips need no more than this: unexplained notes mean a wrong fret, not a muted string.</summary>
        public double MaxUnexplainedForStringTip = 0.15;

        public ChordFeedback Evaluate(ChordShape target, ChordMatchResult match, StringCheckResult strings)
        {
            if (match.BestScore < MinBestScore) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);

            if (!match.MatchesTarget)
            {
                bool confident = match.BestScore >= MinWrongChordScore
                                 && match.BestScore - match.TargetScore > WrongChordMargin
                                 && strings.UnexplainedFraction >= MinUnexplainedForWrongChord;
                if (!confident) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);
                return new ChordFeedback
                {
                    Kind = FeedbackKind.WrongChord,
                    Message = "That sounds like " + match.BestChord + ", not " + target.name + ". Match the ghost hand and try again.",
                    HeardChord = match.BestChord,
                    TargetScore = match.TargetScore
                };
            }

            if (match.TargetScore < MinTargetScore) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);

            bool stringTipOk = strings.UnexplainedFraction <= MaxUnexplainedForStringTip;

            if (strings.UnwantedStrings.Count > 0)
            {
                if (!stringTipOk) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);
                int s = strings.UnwantedStrings[0];
                return new ChordFeedback
                {
                    Kind = FeedbackKind.UnwantedString,
                    StringIndex = s,
                    Message = "Try not to hit the " + GuitarTuning.StringNames[s] + " string for this chord.",
                    HeardChord = match.BestChord,
                    TargetScore = match.TargetScore
                };
            }

            if (strings.WeakStrings.Count > 0)
            {
                if (!stringTipOk) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);
                int s = strings.WeakStrings[0];
                return new ChordFeedback
                {
                    Kind = FeedbackKind.WeakString,
                    StringIndex = s,
                    Message = "The " + GuitarTuning.StringNames[s] + " string is muted. Arch your fingers so they don't touch it.",
                    HeardChord = match.BestChord,
                    TargetScore = match.TargetScore
                };
            }

            if (strings.UnexplainedFraction > MaxUnexplainedForClean) return ChordFeedback.NotSure(match.BestChord, match.TargetScore);

            return new ChordFeedback
            {
                Kind = FeedbackKind.Clean,
                Message = "Clean " + target.name + "!",
                HeardChord = match.BestChord,
                TargetScore = match.TargetScore
            };
        }
    }
}
