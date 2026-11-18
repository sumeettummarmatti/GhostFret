using System;
using System.IO;
using GhostFret.Core.Progress;
using UnityEngine;

namespace GhostFret.Runtime.Progress
{
    /// <summary>
    /// Saves PracticeProgress on the device only (persistentDataPath). Nothing is uploaded,
    /// and no mic, hand or camera data is ever part of it.
    /// Load never crashes: a missing or corrupt file just means fresh progress.
    /// Save is atomic: write a temp file, then swap it in, so a crash can't leave half a file.
    /// </summary>
    public static class ProgressStore
    {
        private const string FileName = "progress.json";

        private static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }
        private static string TempPath { get { return FilePath + ".tmp"; } }

        public static PracticeProgress Load()
        {
            PracticeProgress p = TryRead(FilePath);
            if (p == null) p = TryRead(TempPath);   // crashed between write and swap
            if (p == null) p = new PracticeProgress();
            p.Sanitize();
            return p;
        }

        private static PracticeProgress TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json) || json.Trim().Length == 0) return null;
                return JsonUtility.FromJson<PracticeProgress>(json);
            }
            catch (Exception e)
            {
                // Corrupt or unreadable: start fresh rather than crash. (No personal data in the message.)
                Debug.LogWarning("GhostFret: progress file unreadable, starting fresh (" + e.GetType().Name + ").");
                return null;
            }
        }

        public static bool Save(PracticeProgress progress)
        {
            if (progress == null) return false;
            try
            {
                File.WriteAllText(TempPath, JsonUtility.ToJson(progress, true));
                if (File.Exists(FilePath))
                {
                    try { File.Replace(TempPath, FilePath, null); }
                    catch (Exception)
                    {
                        // Some platforms don't support Replace: fall back to delete + move.
                        File.Delete(FilePath);
                        File.Move(TempPath, FilePath);
                    }
                }
                else File.Move(TempPath, FilePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("GhostFret: could not save progress (" + e.GetType().Name + ").");
                return false;
            }
        }

        /// <summary>"Delete my progress" in Settings.</summary>
        public static void DeleteAll()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("GhostFret: could not delete progress (" + e.GetType().Name + ").");
            }
        }
    }
}
