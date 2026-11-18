using System.IO;
using System.Linq;
using System.Text.Json;
using GhostFret.Core.Content;
using Xunit;

namespace GhostFret.Core.Tests
{
    /// <summary>Validates every song chart and the curriculum in StreamingAssets.</summary>
    public class ContentTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        private static string ContentDir => Path.Combine(System.AppContext.BaseDirectory, "Content");

        private static SongChart[] LoadSongs() =>
            Directory.GetFiles(Path.Combine(ContentDir, "songs"), "*.json")
                .Select(f => JsonSerializer.Deserialize<SongChart>(File.ReadAllText(f), Options))
                .ToArray();

        [Fact]
        public void All_song_charts_are_valid()
        {
            var songs = LoadSongs();
            Assert.NotEmpty(songs);
            var errors = songs.SelectMany(SongValidator.Validate).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }

        [Fact]
        public void Curriculum_songs_exist_and_are_unlocked_on_their_day()
        {
            var songs = LoadSongs().ToDictionary(s => s.id);
            var curriculum = JsonSerializer.Deserialize<Curriculum>(File.ReadAllText(Path.Combine(ContentDir, "curriculum.json")), Options);
            foreach (var day in curriculum.days)
            {
                var learned = curriculum.ChordsLearnedBy(day.day);
                foreach (var id in day.songIds)
                {
                    Assert.True(songs.ContainsKey(id), "Missing song file: " + id);
                    Assert.True(SongValidator.IsUnlocked(songs[id], learned), id + " uses a chord not yet taught by day " + day.day);
                }
            }
        }
    }
}
