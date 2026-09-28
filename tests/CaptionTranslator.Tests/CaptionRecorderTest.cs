using System.Diagnostics;
using System.Text.Json;
using CaptionTranslator.Captions;

namespace CaptionTranslator.Tests
{
    /// <summary>
    /// Records Teams caption snapshots (every 250 ms, 90 s) to %TEMP%\caption-recording.json for replaying through
    /// the stabilizer. The file stays on this PC and is not part of the repository.
    /// </summary>
    [TestClass]
    [TestCategory("Live")]
    public class CaptionRecorderTest
    {
        public static string RecordingPath => Path.Combine(Path.GetTempPath(), "caption-recording.json");

        [TestMethod]
        public void Record_LiveMeeting_SavesSnapshots()
        {
            TeamsCaptionSource source = new TeamsCaptionSource(new AppSettings().TeamsCaptionContainerPattern);
            if (source.Read().Segments.Count == 0)
                Assert.Inconclusive("No Teams meeting with live captions open.");

            List<RecordedSnapshot> snapshots = new List<RecordedSnapshot>();
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(90))
            {
                CaptionReadResult result = source.Read();
                if (result.IsReading)
                    snapshots.Add(new RecordedSnapshot(clock.ElapsedMilliseconds, result.Segments.Select(segment => new[] { segment.Speaker, segment.Text, segment.Id ?? string.Empty }).ToList()));
                Thread.Sleep(250);
            }

            File.WriteAllText(RecordingPath, JsonSerializer.Serialize(snapshots));
            Console.WriteLine($"Recorded {snapshots.Count} snapshots to {RecordingPath}");
        }

        public sealed record RecordedSnapshot(long Milliseconds, List<string[]> Segments);
    }
}
