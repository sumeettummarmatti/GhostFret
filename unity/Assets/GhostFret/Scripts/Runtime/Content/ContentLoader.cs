using System.Collections;
using GhostFret.Core.Content;
using UnityEngine;
using UnityEngine.Networking;

namespace GhostFret.Runtime.Content
{
    /// <summary>
    /// Loads curriculum.json and song charts from StreamingAssets/GhostFret.
    /// On Android (Quest) StreamingAssets live inside the APK, so use UnityWebRequest.
    /// TODO (Phase 5): load the song list from the curriculum's songIds.
    /// </summary>
    public sealed class ContentLoader : MonoBehaviour
    {
        public Curriculum Curriculum { get; private set; }

        public IEnumerator LoadCurriculum()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "GhostFret/curriculum.json");
            using (var request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                    Curriculum = JsonUtility.FromJson<Curriculum>(request.downloadHandler.text);
                else
                    Debug.LogError("Could not load curriculum: " + request.error);
            }
        }

        public IEnumerator LoadSong(string id, System.Action<SongChart> done)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "GhostFret/songs/" + id + ".json");
            using (var request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                done(request.result == UnityWebRequest.Result.Success
                    ? JsonUtility.FromJson<SongChart>(request.downloadHandler.text)
                    : null);
            }
        }
    }
}
