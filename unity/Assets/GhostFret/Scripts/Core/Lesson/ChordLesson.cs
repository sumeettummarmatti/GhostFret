using System.Collections.Generic;
using GhostFret.Core.Feedback;

namespace GhostFret.Core.Lesson
{
    public enum LessonReactionKind { Clean, StringProblem, WrongChord, GeneralTip, ChordLearned, LessonDone }

    /// <summary>What the Chord Coach should show after one strum. One tip at a time.</summary>
    public sealed class LessonReaction
    {
        public LessonReactionKind Kind;
        public string Message;
        public int StringIndex = -1;   // string to highlight amber (−1 = none)
        public string Chord;           // the chord being practised when this happened
        public bool WasClean;
    }

    public struct LessonStep
    {
        public string Chord;
        public int CleanNeeded;
        public bool IsReview;
    }

    /// <summary>
    /// Chord Coach logic (DESIGN.md §7.2): a short queue of chords; each needs N clean strums.
    /// Audio feedback decides correctness. Specific tips only when the evaluator was confident;
    /// "NotSure" becomes a general tip, so we never tell the player something wrong.
    /// </summary>
    public sealed class ChordLesson
    {
        public const int NewChordCleanNeeded = 3;
        public const int ReviewCleanNeeded = 1;

        public readonly List<LessonStep> Steps = new List<LessonStep>();
        public int Index { get; private set; }
        public int CleanCount { get; private set; }
        public bool IsDone { get { return Index >= Steps.Count; } }
        public LessonStep Current { get { return Steps[Index]; } }
        public string CurrentChord { get { return IsDone ? null : Steps[Index].Chord; } }

        public ChordLesson(IEnumerable<LessonStep> steps)
        {
            Steps.AddRange(steps);
        }

        /// <summary>
        /// Today's lesson: a quick review of the previous day's new chords (1 clean each),
        /// then today's new chords (3 clean each). A review-only day (no new chords) reviews every learned chord.
        /// </summary>
        public static ChordLesson ForDay(string[] todaysNewChords, string[] previousDayChords, ICollection<string> allLearnedBefore)
        {
            var steps = new List<LessonStep>();
            var seen = new HashSet<string>();
            bool reviewOnly = todaysNewChords == null || todaysNewChords.Length == 0;

            IEnumerable<string> review = reviewOnly ? (IEnumerable<string>)(allLearnedBefore ?? new string[0]) : previousDayChords;
            if (review != null)
                foreach (string c in review)
                    if (!string.IsNullOrEmpty(c) && seen.Add(c))
                        steps.Add(new LessonStep { Chord = c, CleanNeeded = reviewOnly ? 2 : ReviewCleanNeeded, IsReview = true });

            if (!reviewOnly)
                foreach (string c in todaysNewChords)
                    if (!string.IsNullOrEmpty(c) && seen.Add(c))
                        steps.Add(new LessonStep { Chord = c, CleanNeeded = NewChordCleanNeeded, IsReview = false });

            if (steps.Count == 0) steps.Add(new LessonStep { Chord = "Em", CleanNeeded = NewChordCleanNeeded });
            return new ChordLesson(steps);
        }

        /// <summary>One chord, practised on its own from the menu.</summary>
        public static ChordLesson Single(string chord)
        {
            return new ChordLesson(new[] { new LessonStep { Chord = chord, CleanNeeded = NewChordCleanNeeded } });
        }

        /// <summary>React to one strum's feedback for the current chord.</summary>
        public LessonReaction OnFeedback(ChordFeedback fb)
        {
            if (IsDone || fb == null) return null;
            string chord = CurrentChord;

            switch (fb.Kind)
            {
                case FeedbackKind.Clean:
                    CleanCount++;
                    if (CleanCount < Current.CleanNeeded)
                        return new LessonReaction { Kind = LessonReactionKind.Clean, Chord = chord, WasClean = true,
                            Message = "Clean " + chord + "! " + (Current.CleanNeeded - CleanCount) + " more." };
                    Index++;
                    CleanCount = 0;
                    return new LessonReaction
                    {
                        Kind = IsDone ? LessonReactionKind.LessonDone : LessonReactionKind.ChordLearned,
                        Chord = chord, WasClean = true, Message = "You've got " + chord + "!"
                    };

                case FeedbackKind.WeakString:
                case FeedbackKind.UnwantedString:
                    if (fb.StringIndex < 0 || fb.StringIndex > 5 || string.IsNullOrEmpty(fb.Message)) return General(chord);
                    return new LessonReaction { Kind = LessonReactionKind.StringProblem, Chord = chord,
                        StringIndex = fb.StringIndex, Message = fb.Message };

                case FeedbackKind.WrongChord:
                    if (string.IsNullOrEmpty(fb.Message)) return General(chord);
                    return new LessonReaction { Kind = LessonReactionKind.WrongChord, Chord = chord, Message = fb.Message };

                case FeedbackKind.TooQuiet:
                    return new LessonReaction { Kind = LessonReactionKind.GeneralTip, Chord = chord,
                        Message = string.IsNullOrEmpty(fb.Message) ? "I couldn't hear that. Try a slightly firmer strum." : fb.Message };

                default: // NotSure or anything new: never guess
                    return General(chord);
            }
        }

        private static LessonReaction General(string chord)
        {
            return new LessonReaction { Kind = LessonReactionKind.GeneralTip, Chord = chord,
                Message = "Almost! Check each finger against the ghost hand and strum again." };
        }
    }
}
