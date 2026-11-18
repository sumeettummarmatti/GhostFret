using System;
using System.Collections;
using System.Collections.Generic;
using GhostFret.Core.Content;
using UnityEngine;
using UnityEngine.Networking;

namespace GhostFret.Runtime.Content
{
    /// <summary>
    /// Loads curriculum.json and song charts from StreamingAssets/GhostFret.
    /// On Android (Quest) StreamingAssets live inside the APK ("jar:file://…!/assets"), so everything
    /// goes through UnityWebRequest; in the Editor local paths become file:// URIs (spaces escaped).
    /// Charts are cached, and any chart that fails SongValidator is skipped with a warning, never shown.
    /// Usage: StartCoroutine(loader.LoadAllSongs(songs => ...)).
    /// </summary>
    public sealed class ContentLoader : MonoBehaviour
    {
        public const string Folder = "GhostFret";

        public Curriculum Curriculum { get; private set; }

        /// <summary>Valid songs in curriculum order (filled by LoadAllSongs).</summary>
        public IReadOnlyList<SongChart> Songs { get { return _songs; } }

        public bool SongsLoaded { get; private set; }

        private readonly List<SongChart> _songs = new List<SongChart>();
        private readonly Dictionary<string, SongChart> _cache = new Dictionary<string, SongChart>();
        private readonly HashSet<string> _invalid = new HashSet<string>();

        public IEnumerator LoadCurriculum(Action<Curriculum> done = null)
        {
            if (Curriculum != null) { if (done != null) done(Curriculum); yield break; }

            string text = null;
            yield return Fetch(Folder + "/curriculum.json", t => text = t);
            if (text != null)
            {
                try { Curriculum = JsonUtility.FromJson<Curriculum>(text); }
                catch (Exception e) { Debug.LogError("GhostFret: curriculum.json could not be read: " + e.Message); }
            }
            if (Curriculum != null && Curriculum.days == null) Curriculum.days = new CurriculumDay[0];
            if (done != null) done(Curriculum);
        }

        /// <summary>Loads one chart by id. done(null) if it is missing or fails validation.</summary>
        public IEnumerator LoadSong(string id, Action<SongChart> done)
        {
            SongChart cached;
            if (string.IsNullOrEmpty(id) || !IsSafeId(id) || _invalid.Contains(id))
            {
                if (done != null) done(null);
                yield break;
            }
            if (_cache.TryGetValue(id, out cached)) { if (done != null) done(cached); yield break; }

            string text = null;
            yield return Fetch(Folder + "/songs/" + id + ".json", t => text = t);

            SongChart chart = null;
            if (text != null)
            {
                try { chart = JsonUtility.FromJson<SongChart>(text); }
                catch (Exception e) { Debug.LogWarning("GhostFret: song '" + id + "' could not be read: " + e.Message); }
            }

            if (chart != null)
            {
                List<string> problems = SongValidator.Validate(chart);
                if (problems.Count > 0)
                {
                    Debug.LogWarning("GhostFret: skipping song '" + id + "':\n" + string.Join("\n", problems.ToArray()));
                    chart = null;
                }
            }

            if (chart == null) _invalid.Add(id);
            else _cache[id] = chart;
            if (done != null) done(chart);
        }

        /// <summary>Loads every song listed in the curriculum (loading the curriculum first if needed).</summary>
        public IEnumerator LoadAllSongs(Action<IReadOnlyList<SongChart>> done = null)
        {
            if (Curriculum == null) yield return LoadCurriculum();

            _songs.Clear();
            var seen = new HashSet<string>();
            if (Curriculum != null && Curriculum.days != null)
            {
                foreach (var day in Curriculum.days)
                {
                    if (day == null || day.songIds == null) continue;
                    foreach (string id in day.songIds)
                    {
                        if (!seen.Add(id)) continue;
                        SongChart chart = null;
                        yield return LoadSong(id, c => chart = c);
                        if (chart != null) _songs.Add(chart);
                    }
                }
            }
            SongsLoaded = true;
            if (done != null) done(_songs);
        }

        /// <summary>Songs whose chords have all been learned, in curriculum order.</summary>
        public List<SongChart> UnlockedSongs(ICollection<string> learnedChords)
        {
            var list = new List<SongChart>();
            if (learnedChords == null) return list;
            foreach (var s in _songs) if (SongValidator.IsUnlocked(s, learnedChords)) list.Add(s);
            return list;
        }

        /// <summary>A loaded song by id, or null.</summary>
        public SongChart Find(string id)
        {
            SongChart chart;
            return id != null && _cache.TryGetValue(id, out chart) ? chart : null;
        }

        /// <summary>URL for a file under StreamingAssets that UnityWebRequest can open on every platform.</summary>
        public static string StreamingUrl(string relativePath)
        {
            string root = Application.streamingAssetsPath;
            string joined = root.TrimEnd('/', '\\') + "/" + relativePath;
            // Android: already "jar:file://…". WebGL: "http(s)://…". Desktop/Editor: plain path → file:// URI.
            if (joined.Contains("://")) return joined;
            return new Uri(joined).AbsoluteUri;
        }

        private static IEnumerator Fetch(string relativePath, Action<string> done)
        {
            string url = StreamingUrl(relativePath);
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.text);
                else
                {
                    Debug.LogWarning("GhostFret: could not load " + relativePath + " (" + request.error + ")");
                    done(null);
                }
            }
        }

        // Ids come from our own curriculum, but keep them to plain file names anyway.
        private static bool IsSafeId(string id)
        {
            foreach (char c in id)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            return true;
        }
    }
}
