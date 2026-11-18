using GhostFret.Core.Content;
using GhostFret.Core.Lesson;
using UnityEngine;

namespace GhostFret.Runtime.Song
{
    /// <summary>
    /// Runs a song: beat clock on AudioSettings.dspTime, chord changes, backing track or metronome,
    /// adaptive tempo, section looping (DESIGN.md §7.5).
    /// TODO (Phase 5): build SongTimeline; drive GhostHand (one beat early) + StrumLane;
    ///                 count clean chords from AudioCoach; AdaptiveTempo.RecordRun at the end.
    /// </summary>
    public sealed class SongPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource backing;
        [SerializeField] private AudioSource metronome;

        public SongChart Chart { get; private set; }
        public readonly AdaptiveTempo Tempo = new AdaptiveTempo();

        public void Load(SongChart chart) { Chart = chart; /* TODO */ }
        public void Play() { /* TODO */ }
        public void Pause() { /* TODO */ }
        public void Resume() { /* TODO */ }
    }
}
