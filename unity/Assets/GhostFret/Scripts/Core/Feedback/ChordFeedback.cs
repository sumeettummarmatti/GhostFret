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

        public static ChordFeedback TooQuiet()
        {
            return new ChordFeedback { Kind = FeedbackKind.TooQuiet, Message = "I couldn't hear that. Try a slightly firmer strum." };
        }
    }

    /// <summary>
    /// Turns analysis into friendly, specific feedback.
    /// Tone rules: short, kind, one tip at a time, never shaming.
    /// </summary>
    public sealed class ChordFeedbackEvaluator
    {
        public double MinBestScore = 0.6;   // below this, we're not sure what was played
        public double WrongChordMargin = 0.08;

        public ChordFeedback Evaluate(ChordShape target, ChordMatchResult match, StringCheckResult strings)
        {
            if (match.BestScore < MinBestScore)
            {
                return new ChordFeedback
                {
                    Kind = FeedbackKind.NotSure,
                    Message = "Almost! Check each finger against the ghost hand and strum again.",
                    HeardChord = match.BestChord
                };
            }

            if (!match.MatchesTarget && match.BestScore - match.TargetScore > WrongChordMargin)
            {
                return new ChordFeedback
                {
                    Kind = FeedbackKind.WrongChord,
                    Message = "That sounds like " + match.BestChord + ", not " + target.name + ". Match the ghost hand and try again.",
                    HeardChord = match.BestChord
                };
            }

            if (strings.UnwantedStrings.Count > 0)
            {
                int s = strings.UnwantedStrings[0];
                return new ChordFeedback
                {
                    Kind = FeedbackKind.UnwantedString,
                    StringIndex = s,
                    Message = "Try not to hit the " + GuitarTuning.StringNames[s] + " string for this chord.",
                    HeardChord = match.BestChord
                };
            }

            if (strings.WeakStrings.Count > 0)
            {
                int s = strings.WeakStrings[0];
                return new ChordFeedback
                {
                    Kind = FeedbackKind.WeakString,
                    StringIndex = s,
                    Message = "The " + GuitarTuning.StringNames[s] + " string is muted. Arch your fingers so they don't touch it.",
                    HeardChord = match.BestChord
                };
            }

            return new ChordFeedback
            {
                Kind = FeedbackKind.Clean,
                Message = "Clean " + target.name + "!",
                HeardChord = match.BestChord
            };
        }
    }
}
