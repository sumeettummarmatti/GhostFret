namespace GhostFret.Core.Lesson
{
    /// <summary>
    /// "How many clean switches between two chords in 60 seconds?"
    /// The first clean chord starts the chain; every clean switch after that scores a point.
    /// </summary>
    public sealed class ChangeDrill
    {
        public readonly string ChordA;
        public readonly string ChordB;
        public readonly double DurationSeconds;

        public string ExpectedChord { get; private set; }
        public int CleanChanges { get; private set; }
        public bool IsRunning { get; private set; }

        private double _startTime;
        private bool _chainStarted;

        public ChangeDrill(string chordA, string chordB, double durationSeconds = 60)
        {
            ChordA = chordA;
            ChordB = chordB;
            DurationSeconds = durationSeconds;
        }

        public void Start(double nowSeconds)
        {
            _startTime = nowSeconds;
            ExpectedChord = ChordA;
            CleanChanges = 0;
            _chainStarted = false;
            IsRunning = true;
        }

        public double TimeLeft(double nowSeconds)
        {
            double left = DurationSeconds - (nowSeconds - _startTime);
            return left > 0 ? left : 0;
        }

        /// <summary>True once the 60 s are used up (stops the drill).</summary>
        public bool IsTimeUp(double nowSeconds)
        {
            if (IsRunning && TimeLeft(nowSeconds) <= 0) IsRunning = false;
            return !IsRunning;
        }

        /// <summary>"Em-D", the key used for the saved best score.</summary>
        public string PairKey { get { return ChordA + "-" + ChordB; } }

        /// <summary>Call with each clean chord the coach hears. Returns true if it scored a point.</summary>
        public bool OnCleanChord(string chordName, double nowSeconds)
        {
            if (!IsRunning) return false;
            if (TimeLeft(nowSeconds) <= 0) { IsRunning = false; return false; }
            if (chordName != ExpectedChord) return false;

            bool scored = _chainStarted;
            if (scored) CleanChanges++;
            _chainStarted = true;
            ExpectedChord = ExpectedChord == ChordA ? ChordB : ChordA;
            return scored;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
