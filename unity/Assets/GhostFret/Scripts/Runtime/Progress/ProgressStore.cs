using System.IO;
using GhostFret.Core.Progress;
using UnityEngine;

namespace GhostFret.Runtime.Progress
{
    /// <summary>Saves PracticeProgress on the device only (persistentDataPath). Nothing is uploaded.</summary>
    public static class ProgressStore
    {
        private static string FilePath { get { return Path.Combine(Application.persistentDataPath, "progress.json"); } }

        public static PracticeProgress Load()
        {
            if (!File.Exists(FilePath)) return new PracticeProgress();
            return JsonUtility.FromJson<PracticeProgress>(File.ReadAllText(FilePath)) ?? new PracticeProgress();
        }

        public static void Save(PracticeProgress progress)
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(progress, true));
        }

        public static void DeleteAll()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
